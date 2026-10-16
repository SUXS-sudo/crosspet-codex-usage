"""钩子读事件（#19）：有的宿主（Windows 上 Codex 的 app-server）把事件 JSON 写完不关输入，
钩子要读到一个完整的 JSON 就处理、退出，不能等输入关闭——不然进程一直挂着，每次留下一个空白终端。
    python3 -m unittest tests.hook_stdin_test
"""
import json
import os
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

HOOK = Path(__file__).resolve().parent.parent / "integrations" / "crosspet-hook.py"
EVENT = {"hook_event_name": "PostToolUse", "session_id": "stdin-test", "tool_name": "Bash", "cwd": "/tmp"}


class HookStdinTest(unittest.TestCase):
    def setUp(self):
        self.state = tempfile.TemporaryDirectory()
        # 只发 PostToolUse：它不在 LAUNCH_EVENTS 里，钩子不会去打开真的桌宠
        self.env = dict(os.environ, CROSSPET_STATE_DIR=self.state.name, CROSSPET_DATA_DIR=self.state.name)

    def tearDown(self):
        self.state.cleanup()

    def start(self):
        return subprocess.Popen([sys.executable, str(HOOK), "gpt"], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                stderr=subprocess.PIPE, env=self.env)

    def finish(self, p):
        try:
            p.stdin.close()
        except OSError:
            pass
        p.wait(timeout=10)
        p.stdout.close()
        p.stderr.close()

    def state_written(self):
        return Path(self.state.name, "gpt-state.json").exists()

    def test_complete_json_without_eof_is_handled(self):
        p = self.start()
        try:
            p.stdin.write(json.dumps(EVENT).encode("utf-8"))
            p.stdin.flush()   # 写完整个 JSON，但不关输入
            p.wait(timeout=3)
            self.assertTrue(self.state_written(), "完整的事件应该已经写进状态")
        except subprocess.TimeoutExpired:
            self.fail("写完完整 JSON 后钩子还在等输入关闭")
        finally:
            self.finish(p)

    def test_json_split_across_writes_and_utf8_boundary(self):
        data = json.dumps({**EVENT, "prompt": "中文内容"}, ensure_ascii=False).encode("utf-8")
        cut = data.index("中".encode("utf-8")) + 1   # 从一个汉字中间切开
        p = self.start()
        try:
            p.stdin.write(data[:cut]); p.stdin.flush()
            time.sleep(0.3)
            p.stdin.write(data[cut:]); p.stdin.flush()
            p.wait(timeout=3)
            self.assertTrue(self.state_written())
        except subprocess.TimeoutExpired:
            self.fail("分两次写、从汉字中间切开时没处理")
        finally:
            self.finish(p)

    def test_silent_open_pipe_times_out(self):
        p = self.start()   # 什么都不写，也不关
        try:
            t = time.monotonic()
            p.wait(timeout=5)
            self.assertLess(time.monotonic() - t, 5)
            self.assertFalse(self.state_written())
        except subprocess.TimeoutExpired:
            self.fail("空着的输入一直不关时，钩子应该限时退出")
        finally:
            self.finish(p)

    def test_normal_pipe_with_eof_still_works(self):
        r = subprocess.run([sys.executable, str(HOOK), "gpt"], input=json.dumps(EVENT).encode("utf-8"),
                           capture_output=True, env=self.env, timeout=10)
        self.assertEqual(r.returncode, 0)
        self.assertTrue(self.state_written())

    def test_invalid_json_writes_nothing(self):
        r = subprocess.run([sys.executable, str(HOOK), "gpt"], input=b"{not json", capture_output=True, env=self.env, timeout=10)
        self.assertEqual(r.returncode, 0)
        self.assertFalse(self.state_written())

    def test_large_event_is_read_whole(self):
        big = {**EVENT, "tool_response": "x" * (3 << 20)}   # 3 MB 的工具输出（Claude Code 的 PostToolUse 会带）
        r = subprocess.run([sys.executable, str(HOOK), "gpt"], input=json.dumps(big).encode("utf-8"),
                           capture_output=True, env=self.env, timeout=20)
        self.assertEqual(r.returncode, 0)
        self.assertTrue(self.state_written())


if __name__ == "__main__":
    unittest.main()
