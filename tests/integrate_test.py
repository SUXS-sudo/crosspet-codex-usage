import contextlib
import os
import importlib.util
import io
import tempfile
import unittest
from unittest import mock
from pathlib import Path

spec = importlib.util.spec_from_file_location('integrate', Path(__file__).resolve().parents[1] / 'tools/integrate.py')
integrate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(integrate)

class IntegrationTests(unittest.TestCase):
    # 插件版本检查（只读）：自带的 / 装到 Harness 那边的 / Harness 里正在跑的，设置页和桌宠据此提醒用户点「更新」
    def _plugin_dirs(self, tmp, bundled='1.3.5', installed='1.3.4', running='1.3.2'):
        b = Path(tmp) / 'bundled.js'; b.write_text(f'const PLUGIN_VERSION = "{bundled}";')
        d = Path(tmp) / 'plugin'
        if installed:
            (d / 'lib').mkdir(parents=True); (d / 'lib/index.js').write_text(f'const PLUGIN_VERSION = "{installed}";  // x')
        st = Path(tmp) / 'state'; st.mkdir()
        if running: (st / 'deepseek-plugin.json').write_text('{"plugin": "%s", "apiKey": "ok"}' % running)
        return b, d, st
    def test_plugin_status_reports_three_versions(self):
        with tempfile.TemporaryDirectory() as tmp:
            b, d, st = self._plugin_dirs(tmp)
            got = integrate.plugin_status(['deepseek'], bundled=b, plugin_dir=d, state_dir=st)
        self.assertEqual(got, [{'id': 'deepseek', 'label': 'DeepSeek Harness', 'bundled': '1.3.5', 'installed': '1.3.4', 'running': '1.3.2'}])
    def test_plugin_status_only_for_integrated(self):
        with tempfile.TemporaryDirectory() as tmp:
            b, d, st = self._plugin_dirs(tmp)
            self.assertEqual(integrate.plugin_status(['codex'], bundled=b, plugin_dir=d, state_dir=st), [])
    def test_plugin_status_missing_files_are_none(self):
        with tempfile.TemporaryDirectory() as tmp:
            b, d, st = self._plugin_dirs(tmp, installed=None, running=None)
            got = integrate.plugin_status(['deepseek'], bundled=b, plugin_dir=d, state_dir=st)
        self.assertEqual(got[0]['installed'], None); self.assertEqual(got[0]['running'], None); self.assertEqual(got[0]['bundled'], '1.3.5')
    def test_plugin_status_writes_nothing(self):
        with tempfile.TemporaryDirectory() as tmp:
            b, d, st = self._plugin_dirs(tmp)
            before = sorted((p, p.stat().st_mtime_ns) for p in Path(tmp).rglob('*'))
            integrate.plugin_status(['deepseek'], bundled=b, plugin_dir=d, state_dir=st)
            self.assertEqual(sorted((p, p.stat().st_mtime_ns) for p in Path(tmp).rglob('*')), before)
    def test_codex_programs_skips_desktop_gui_exe(self):
        # Codex 桌面版的图形界面程序旁边有 resources.pak 等文件：不能当命令行跑 --version（会弹窗口）
        with tempfile.TemporaryDirectory() as tmp:
            exe = Path(tmp) / 'codex'
            exe.write_text('#!/bin/sh\necho should-not-run\n'); exe.chmod(0o755)
            (Path(tmp) / 'resources.pak').write_text('')
            with mock.patch.dict(os.environ, {'CROSSPET_CODEX_BIN': str(exe)}):
                found = integrate.codex_programs()
        self.assertFalse(any(str(exe) in f for f in found), found)
    def test_codex_programs_lists_override_with_version(self):
        fake = str(Path(__file__).resolve().parent / 'fake_codex.py')
        with mock.patch.dict(os.environ, {'CROSSPET_CODEX_BIN': fake}):
            found = integrate.codex_programs()
        self.assertTrue(found and found[0].startswith(fake), found)
        self.assertIn('codex-cli 0.0.0-fake', found[0])
    def test_preserve_other_hook_in_same_group(self):
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp) / 'settings.json'
            other = {'type': 'command', 'command': 'echo keep-me'}
            integrate.save_json(p, {'theme': 'dark', 'hooks': {'Stop': [{'matcher': '*', 'hooks': [other, {'command': 'python crosspet-hook.py'}]}]}})
            integrate.remove_hooks(p)
            self.assertEqual(integrate.load_json(p), {'theme': 'dark', 'hooks': {'Stop': [{'matcher': '*', 'hooks': [other]}]}})
    def test_repeated_install_is_idempotent(self):
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp) / 'settings.json'
            template = integrate.REPO / 'integrations/codex/hooks.json'
            integrate.merge_hooks(p, template)
            first = integrate.load_json(p)
            integrate.merge_hooks(p, template)
            self.assertEqual(first, integrate.load_json(p))

# Harness 加的服务商写在文件末尾，也就是我们那段的两行注释中间
DSH_USER = """- insert:
    - id: hello
      name: '@deepseek-ai/hello'
"""
DSH_PROVIDER = """- insert:
    - id: provider-siliconflow
      name: '@deepseek-ai/dsh-provider-openai'
      config:
        baseURL: https://api.siliconflow.cn/v1
        apiKey: sk-xxx
"""
DSH_OURS_ONLY = "    - id: crosspet\n      name: '@local/dsh-plugin-crosspet'\n"


class DeepSeekPatchTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        root = Path(self.tmp.name)
        self.profile = root / 'profile'
        self.profile.mkdir()
        self.patch = self.profile / 'cordis.patch.yml'
        integrate.save_json(self.profile / 'package.json', {'dependencies': {}})
        self.saved = {k: getattr(integrate, k) for k in ('DSH_PROFILE', 'DSH_PLUGIN_DIR', 'DSH_RESTORED')}
        integrate.DSH_PROFILE = self.profile
        integrate.DSH_PLUGIN_DIR = root / 'support/dsh-plugin-crosspet'
        integrate.DSH_RESTORED = root / 'support/dsh-patch-restored'

    def tearDown(self):
        for k, v in self.saved.items():
            setattr(integrate, k, v)
        self.tmp.cleanup()

    def run_dsh(self, install):
        with contextlib.redirect_stdout(io.StringIO()) as out:
            integrate.deepseek(install)
        return out.getvalue()

    def test_strip_keeps_provider_between_markers(self):
        text = DSH_USER + integrate.DSH_BLOCK.replace(integrate.DSH_BLOCK_END, DSH_PROVIDER + integrate.DSH_BLOCK_END)
        self.assertEqual(integrate.dsh_strip(text), DSH_USER + DSH_PROVIDER)

    def test_strip_keeps_provider_hung_under_our_insert(self):
        text = DSH_USER + integrate.DSH_BLOCK.replace(DSH_OURS_ONLY, DSH_OURS_ONLY + DSH_PROVIDER.split('\n', 1)[1])
        self.assertEqual(integrate.dsh_strip(text), DSH_USER + DSH_PROVIDER)
        text = DSH_USER + integrate.DSH_BLOCK.replace(DSH_OURS_ONLY, DSH_PROVIDER.split('\n', 1)[1] + DSH_OURS_ONLY)
        self.assertEqual(integrate.dsh_strip(text), DSH_USER + DSH_PROVIDER)

    def test_strip_plain_block_restores_original(self):
        self.assertEqual(integrate.dsh_strip(DSH_USER + integrate.DSH_BLOCK), DSH_USER)
        self.assertEqual(integrate.dsh_strip(integrate.DSH_BLOCK), '')

    def test_refresh_and_uninstall_keep_provider(self):
        self.patch.write_text(DSH_USER, encoding='utf-8')
        self.run_dsh(True)
        installed = self.patch.read_text(encoding='utf-8')
        self.assertEqual(installed, DSH_USER + integrate.DSH_BLOCK)
        # 用户在 Harness 里加了服务商：落在我们两行注释中间
        self.patch.write_text(installed.replace(integrate.DSH_BLOCK_END, DSH_PROVIDER + integrate.DSH_BLOCK_END), encoding='utf-8')
        self.run_dsh(True)   # 更新时的刷新
        self.run_dsh(True)
        after = self.patch.read_text(encoding='utf-8')
        self.assertEqual(after, DSH_USER + DSH_PROVIDER + integrate.DSH_BLOCK)
        self.run_dsh(False)
        self.assertEqual(self.patch.read_text(encoding='utf-8'), DSH_USER + DSH_PROVIDER)

    def test_restore_provider_lost_by_old_version_once(self):
        # 1.3.0 刷新前留下的备份里，服务商夹在我们两行注释中间；刷新后文件里没了
        bak = self.profile / 'cordis.patch.yml.bak-crosspet-20261010-205029'
        bak.write_text(DSH_USER + integrate.DSH_BLOCK.replace(integrate.DSH_BLOCK_END, DSH_PROVIDER + integrate.DSH_BLOCK_END), encoding='utf-8')
        self.patch.write_text(DSH_USER + integrate.DSH_BLOCK, encoding='utf-8')
        out = self.run_dsh(True)
        self.assertIn('找回 1 条', out)
        self.assertEqual(self.patch.read_text(encoding='utf-8'), DSH_USER + DSH_PROVIDER + integrate.DSH_BLOCK)
        # 只找回一次：之后用户自己删掉的不再加回来
        self.patch.write_text(DSH_USER + integrate.DSH_BLOCK, encoding='utf-8')
        self.run_dsh(True)
        self.assertEqual(self.patch.read_text(encoding='utf-8'), DSH_USER + integrate.DSH_BLOCK)

    def test_restore_skips_provider_user_already_readded(self):
        bak = self.profile / 'cordis.patch.yml.bak-crosspet-20261010-205029'
        bak.write_text(DSH_USER + integrate.DSH_BLOCK.replace(integrate.DSH_BLOCK_END, DSH_PROVIDER + integrate.DSH_BLOCK_END), encoding='utf-8')
        readded = DSH_PROVIDER.replace('sk-xxx', 'sk-new')
        self.patch.write_text(DSH_USER + integrate.DSH_BLOCK + readded, encoding='utf-8')
        out = self.run_dsh(True)
        self.assertNotIn('找回', out)
        self.assertEqual(self.patch.read_text(encoding='utf-8'), DSH_USER + readded + integrate.DSH_BLOCK)


if __name__ == '__main__':
    unittest.main()
