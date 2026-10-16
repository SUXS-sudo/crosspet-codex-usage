using System.Windows;
using System.Windows.Markup;

namespace CodexUsageViewer;

internal static class UsageTheme
{
    internal static ResourceDictionary Create()
    {
        return (ResourceDictionary)XamlReader.Parse(@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='Button'>
    <Setter Property='Foreground' Value='{DynamicResource PetColor355E43}'/><Setter Property='Background' Value='White'/><Setter Property='BorderBrush' Value='{DynamicResource PetColorDCE3DA}'/><Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
      <Border x:Name='Frame' CornerRadius='16' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'>
        <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='Opacity' Value='0.82'/></Trigger>
        <Trigger Property='IsPressed' Value='True'><Setter TargetName='Frame' Property='Opacity' Value='0.65'/></Trigger>
        <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='{DynamicResource PetColor355E43}'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Frame' Property='Opacity' Value='0.45'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='TextBox'>
    <Setter Property='Background' Value='White'/><Setter Property='Foreground' Value='{DynamicResource PetColor23262A}'/><Setter Property='BorderBrush' Value='{DynamicResource PetColorDCE3DA}'/><Setter Property='BorderThickness' Value='1'/><Setter Property='SelectionBrush' Value='{DynamicResource PetColorC7DDCD}'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'>
      <Border x:Name='Frame' CornerRadius='10' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'>
        <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='{DynamicResource PetColor539169}'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Frame' Property='Opacity' Value='0.45'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='ComboBox'>
    <Setter Property='Foreground' Value='{DynamicResource PetColor424B44}'/><Setter Property='Background' Value='White'/><Setter Property='BorderBrush' Value='{DynamicResource PetColorDCE3DA}'/><Setter Property='Padding' Value='10,0,25,0'/><Setter Property='ScrollViewer.CanContentScroll' Value='True'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'>
      <Grid>
        <ToggleButton Focusable='False' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}' ClickMode='Press'>
          <ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border x:Name='Frame' Background='White' BorderBrush='{DynamicResource PetColorDCE3DA}' BorderThickness='1' CornerRadius='10'><TextBlock Text='⌄' Foreground='{DynamicResource PetColor697568}' Margin='0,0,10,3' HorizontalAlignment='Right' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='Background' Value='{DynamicResource PetColorF0F5EE}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template>
        </ToggleButton>
        <ContentPresenter IsHitTestVisible='False' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' Margin='{TemplateBinding Padding}' VerticalAlignment='Center'/>
        <Popup x:Name='PART_Popup' IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'>
          <Border Background='White' BorderBrush='{DynamicResource PetColorDCE3DA}' BorderThickness='1' CornerRadius='10' Padding='4' MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' Margin='0,4,0,0'>
            <ScrollViewer MaxHeight='260'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer>
          </Border>
        </Popup>
      </Grid>
      <ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='ComboBoxItem'>
    <Setter Property='Padding' Value='8,7'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'><Border x:Name='Frame' CornerRadius='6' Padding='{TemplateBinding Padding}' Background='Transparent'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Frame' Property='Background' Value='{DynamicResource PetColorEBF2E9}'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter TargetName='Frame' Property='Background' Value='{DynamicResource PetColorDDEBDD}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='DatePicker'><Setter Property='Background' Value='White'/><Setter Property='BorderBrush' Value='{DynamicResource PetColorDCE3DA}'/><Setter Property='Foreground' Value='{DynamicResource PetColor424B44}'/><Setter Property='Padding' Value='3,1'/></Style>
  <Style TargetType='CheckBox'>
    <Setter Property='Foreground' Value='{DynamicResource PetColor526452}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'><StackPanel Orientation='Horizontal' VerticalAlignment='Center'><Border x:Name='Box' Width='16' Height='16' Background='White' BorderBrush='{DynamicResource PetColorC9D7C6}' BorderThickness='1' CornerRadius='5'><TextBlock x:Name='Tick' Text='✓' FontSize='12' Foreground='White' Visibility='Hidden' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ContentPresenter Margin='7,0,0,0' VerticalAlignment='Center'/></StackPanel><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Box' Property='Background' Value='{DynamicResource PetColor539169}'/><Setter TargetName='Box' Property='BorderBrush' Value='{DynamicResource PetColor539169}'/><Setter TargetName='Tick' Property='Visibility' Value='Visible'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='{DynamicResource PetColor355E43}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='ProgressBar'>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ProgressBar'><Grid x:Name='PART_Track' ClipToBounds='True'><Border Background='{TemplateBinding Background}' CornerRadius='3'/><Border x:Name='PART_Indicator' Background='{TemplateBinding Foreground}' CornerRadius='3' HorizontalAlignment='Left'/></Grid></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='DataGridColumnHeader'>
    <Setter Property='Background' Value='{DynamicResource PetColorF2F4ED}'/><Setter Property='Foreground' Value='{DynamicResource PetColor687263}'/><Setter Property='FontSize' Value='11'/><Setter Property='FontWeight' Value='SemiBold'/><Setter Property='Height' Value='32'/><Setter Property='Padding' Value='8,0'/><Setter Property='BorderThickness' Value='0'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='DataGridColumnHeader'><Grid Background='{TemplateBinding Background}'><ContentPresenter Margin='{TemplateBinding Padding}' VerticalAlignment='Center'/><Thumb x:Name='PART_LeftHeaderGripper' HorizontalAlignment='Left' Width='3' Opacity='0'/><Thumb x:Name='PART_RightHeaderGripper' HorizontalAlignment='Right' Width='3' Opacity='0'/></Grid></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='DataGridRow'>
    <Setter Property='Background' Value='White'/><Setter Property='Foreground' Value='{DynamicResource PetColor303A30}'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='DataGridRow'><Border x:Name='Frame' Background='{TemplateBinding Background}' CornerRadius='6'><DataGridCellsPresenter/></Border><ControlTemplate.Triggers><Trigger Property='AlternationIndex' Value='1'><Setter Property='Background' Value='{DynamicResource PetColorFAFAF7}'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='{DynamicResource PetColorF0F5EE}'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter Property='Background' Value='{DynamicResource PetColorE2EEDF}'/><Setter Property='Foreground' Value='{DynamicResource PetColor2C5737}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='DataGridCell'>
    <Setter Property='BorderThickness' Value='0'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='Background' Value='Transparent'/><Setter Property='Foreground' Value='{Binding Foreground, RelativeSource={RelativeSource AncestorType=DataGridRow}}'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='DataGridCell'><Border Background='{TemplateBinding Background}'><ContentPresenter/></Border></ControlTemplate></Setter.Value></Setter>
  </Style>
</ResourceDictionary>");
    }
}
