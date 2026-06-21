Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = "Stop"

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class TomatoPowerSetting {
    public const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;
    public const int WM_POWERBROADCAST = 0x0218;
    public const int PBT_POWERSETTINGCHANGE = 0x8013;
    public static readonly Guid GUID_CONSOLE_DISPLAY_STATE = new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid PowerSettingGuid, int Flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterPowerSettingNotification(IntPtr Handle);

    public static Guid ReadPowerSettingGuid(IntPtr lParam) {
        return (Guid)Marshal.PtrToStructure(lParam, typeof(Guid));
    }

    public static int ReadPowerSettingValue(IntPtr lParam) {
        return Marshal.ReadInt32(lParam, 20);
    }
}
"@

if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne [Threading.ApartmentState]::STA) {
    Start-Process -FilePath "powershell.exe" -ArgumentList @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-STA",
        "-File", "`"$($MyInvocation.MyCommand.Path)`""
    )
    exit
}

$appDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$settingsPath = Join-Path $appDir "settings.json"
$sleepGapSeconds = 10

function Get-Settings {
    $defaults = @{
        WorkMinutes = 25
        BreakMinutes = 5
        Topmost = $true
    }

    if (Test-Path -LiteralPath $settingsPath) {
        try {
            $loaded = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
            if ($loaded.WorkMinutes -ge 1) { $defaults.WorkMinutes = [int]$loaded.WorkMinutes }
            if ($loaded.BreakMinutes -ge 1) { $defaults.BreakMinutes = [int]$loaded.BreakMinutes }
            if ($null -ne $loaded.Topmost) { $defaults.Topmost = [bool]$loaded.Topmost }
        } catch {
            # Bad settings should not prevent the timer from opening.
        }
    }

    return $defaults
}

function Save-Settings {
    param(
        [int]$WorkMinutes,
        [int]$BreakMinutes,
        [bool]$Topmost
    )

    @{
        WorkMinutes = $WorkMinutes
        BreakMinutes = $BreakMinutes
        Topmost = $Topmost
    } | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding UTF8
}

$settings = Get-Settings

$xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Tomato Clock"
        Width="330"
        Height="430"
        MinWidth="300"
        MinHeight="380"
        WindowStartupLocation="CenterScreen"
        ResizeMode="CanResizeWithGrip"
        AllowsTransparency="True"
        WindowStyle="None"
        Background="Transparent"
        ShowInTaskbar="True"
        FontFamily="Segoe UI">
    <Border CornerRadius="18" Background="#F7F2EA" BorderBrush="#202020" BorderThickness="1">
        <Border.Effect>
            <DropShadowEffect Color="#222222" BlurRadius="22" ShadowDepth="7" Opacity="0.18"/>
        </Border.Effect>
        <Grid Margin="16">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>

            <Grid x:Name="TitleBar" Height="36" Cursor="SizeAll">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <TextBlock Text="Tomato Clock" FontSize="16" FontWeight="SemiBold" VerticalAlignment="Center" Foreground="#202020"/>
                <Button x:Name="PinButton" Grid.Column="1" Width="34" Height="30" Margin="0,0,6,0" Content="Pin" ToolTip="Toggle always on top" Background="#EFE5D8" BorderBrush="#202020"/>
                <Button x:Name="CloseButton" Grid.Column="2" Width="34" Height="30" Content="X" ToolTip="Close" Background="#202020" Foreground="#FFFFFF" BorderBrush="#202020"/>
            </Grid>

            <Grid Grid.Row="1" Margin="0,12,0,12">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>
                <TextBlock x:Name="ModeLabel" Text="Focus" HorizontalAlignment="Center" FontSize="17" FontWeight="SemiBold" Foreground="#B53B2D"/>
                <Viewbox Grid.Row="1" Stretch="Uniform" Margin="10">
                    <TextBlock x:Name="TimeLabel" Text="25:00" FontSize="74" FontWeight="Bold" Foreground="#202020"/>
                </Viewbox>
                <TextBlock x:Name="StatusLabel" Grid.Row="2" Text="Ready" HorizontalAlignment="Center" TextWrapping="Wrap" Foreground="#6C6258" FontSize="13"/>
            </Grid>

            <Grid Grid.Row="2" Margin="0,0,0,12">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <Button x:Name="StartPauseButton" Grid.Column="0" Height="40" Margin="0,0,6,0" Content="Start" Background="#B53B2D" Foreground="#FFFFFF" BorderBrush="#B53B2D" FontWeight="SemiBold"/>
                <Button x:Name="ResetButton" Grid.Column="1" Height="40" Margin="3,0,3,0" Content="Reset" Background="#FFFFFF" Foreground="#202020" BorderBrush="#202020"/>
                <Button x:Name="SwitchButton" Grid.Column="2" Height="40" Margin="6,0,0,0" Content="Switch" Background="#EFE5D8" Foreground="#202020" BorderBrush="#202020"/>
            </Grid>

            <Border Grid.Row="3" CornerRadius="12" Background="#FFFDF9" BorderBrush="#D7CBBB" BorderThickness="1" Padding="12">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="82"/>
                    </Grid.ColumnDefinitions>

                    <TextBlock Grid.Row="0" Grid.Column="0" Text="Focus minutes" VerticalAlignment="Center" Foreground="#202020"/>
                    <TextBox x:Name="WorkInput" Grid.Row="0" Grid.Column="1" Height="30" TextAlignment="Center" VerticalContentAlignment="Center" Background="#FFFFFF" BorderBrush="#B7A996"/>

                    <TextBlock Grid.Row="1" Grid.Column="0" Text="Break minutes" Margin="0,10,0,0" VerticalAlignment="Center" Foreground="#202020"/>
                    <TextBox x:Name="BreakInput" Grid.Row="1" Grid.Column="1" Height="30" Margin="0,10,0,0" TextAlignment="Center" VerticalContentAlignment="Center" Background="#FFFFFF" BorderBrush="#B7A996"/>

                    <Button x:Name="ApplyButton" Grid.Row="2" Grid.ColumnSpan="2" Height="34" Margin="0,12,0,0" Content="Apply settings" Background="#202020" Foreground="#FFFFFF" BorderBrush="#202020"/>
                </Grid>
            </Border>
        </Grid>
    </Border>
</Window>
"@

$reader = [System.Xml.XmlReader]::Create([System.IO.StringReader]::new($xaml))
$window = [Windows.Markup.XamlReader]::Load($reader)

$titleBar = $window.FindName("TitleBar")
$pinButton = $window.FindName("PinButton")
$closeButton = $window.FindName("CloseButton")
$modeLabel = $window.FindName("ModeLabel")
$timeLabel = $window.FindName("TimeLabel")
$statusLabel = $window.FindName("StatusLabel")
$startPauseButton = $window.FindName("StartPauseButton")
$resetButton = $window.FindName("ResetButton")
$switchButton = $window.FindName("SwitchButton")
$workInput = $window.FindName("WorkInput")
$breakInput = $window.FindName("BreakInput")
$applyButton = $window.FindName("ApplyButton")

$script:workMinutes = [int]$settings.WorkMinutes
$script:breakMinutes = [int]$settings.BreakMinutes
$script:isWorkMode = $true
$script:isRunning = $false
$script:elapsedSeconds = 0.0
$script:lastTick = Get-Date
$script:notifyIcon = $null
$script:powerModeHandler = $null
$script:sessionSwitchHandler = $null
$script:powerNotificationHandle = [IntPtr]::Zero

$window.Topmost = [bool]$settings.Topmost
$workInput.Text = [string]$script:workMinutes
$breakInput.Text = [string]$script:breakMinutes

function Get-CurrentDurationSeconds {
    if ($script:isWorkMode) {
        return $script:workMinutes * 60
    }
    return $script:breakMinutes * 60
}

function Format-Time {
    param([int]$Seconds)
    if ($Seconds -lt 0) { $Seconds = 0 }
    $minutes = [Math]::Floor($Seconds / 60)
    $remainingSeconds = $Seconds % 60
    return "{0:00}:{1:00}" -f $minutes, $remainingSeconds
}

function Update-Display {
    $duration = Get-CurrentDurationSeconds
    $remaining = [int][Math]::Ceiling($duration - $script:elapsedSeconds)

    $timeLabel.Text = Format-Time $remaining
    if ($script:isWorkMode) {
        $modeLabel.Text = "Focus"
        $modeLabel.Foreground = [Windows.Media.BrushConverter]::new().ConvertFromString("#B53B2D")
    } else {
        $modeLabel.Text = "Break"
        $modeLabel.Foreground = [Windows.Media.BrushConverter]::new().ConvertFromString("#22745B")
    }

    $startPauseButton.Content = $(if ($script:isRunning) { "Pause" } else { "Start" })
}

function Reset-Timer {
    param([string]$Reason = "Reset to zero")
    $script:isRunning = $false
    $script:elapsedSeconds = 0.0
    $script:lastTick = Get-Date
    $statusLabel.Text = $Reason
    Update-Display
}

function Switch-Mode {
    $script:isWorkMode = -not $script:isWorkMode
    Reset-Timer $(if ($script:isWorkMode) { "Switched to focus" } else { "Switched to break" })
}

function Complete-Round {
    [System.Media.SystemSounds]::Asterisk.Play()
    $script:isWorkMode = -not $script:isWorkMode
    $script:elapsedSeconds = 0.0
    $script:lastTick = Get-Date
    $statusLabel.Text = $(if ($script:isWorkMode) { "Break complete. Focus starts now." } else { "Focus complete. Break starts now." })
    Update-Display
}

function Read-Minutes {
    param(
        [string]$Value,
        [string]$Name
    )

    $parsed = 0
    if (-not [int]::TryParse($Value.Trim(), [ref]$parsed) -or $parsed -lt 1 -or $parsed -gt 999) {
        throw "$Name must be an integer from 1 to 999."
    }
    return $parsed
}

$timer = [Windows.Threading.DispatcherTimer]::new()
$timer.Interval = [TimeSpan]::FromMilliseconds(500)
$timer.Add_Tick({
    $now = Get-Date
    $gap = ($now - $script:lastTick).TotalSeconds

    if ($gap -ge $sleepGapSeconds) {
        Reset-Timer "Sleep, display off, or long pause detected. Reset to zero."
        return
    }

    if ($script:isRunning) {
        $script:elapsedSeconds += [Math]::Max(0, $gap)
        if ($script:elapsedSeconds -ge (Get-CurrentDurationSeconds)) {
            Complete-Round
        }
    }

    $script:lastTick = $now
    Update-Display
})

$titleBar.Add_MouseLeftButtonDown({
    if ($_.ButtonState -eq [Windows.Input.MouseButtonState]::Pressed) {
        $window.DragMove()
    }
})

$pinButton.Add_Click({
    $window.Topmost = -not $window.Topmost
    Save-Settings $script:workMinutes $script:breakMinutes $window.Topmost
    $statusLabel.Text = $(if ($window.Topmost) { "Always on top enabled" } else { "Always on top disabled" })
})

$closeButton.Add_Click({ $window.Close() })

$startPauseButton.Add_Click({
    $script:isRunning = -not $script:isRunning
    $script:lastTick = Get-Date
    $statusLabel.Text = $(if ($script:isRunning) { "Running" } else { "Paused" })
    Update-Display
})

$resetButton.Add_Click({ Reset-Timer "Reset to zero" })
$switchButton.Add_Click({ Switch-Mode })

$applyButton.Add_Click({
    try {
        $newWork = Read-Minutes $workInput.Text "Focus minutes"
        $newBreak = Read-Minutes $breakInput.Text "Break minutes"
        $script:workMinutes = $newWork
        $script:breakMinutes = $newBreak
        Save-Settings $script:workMinutes $script:breakMinutes $window.Topmost
        Reset-Timer "Settings applied. Reset to zero."
    } catch {
        $statusLabel.Text = $_.Exception.Message
    }
})

$window.Add_SourceInitialized({
    $source = [Windows.Interop.HwndSource]::FromVisual($window)
    $source.AddHook({
        param(
            [IntPtr]$hwnd,
            [int]$msg,
            [IntPtr]$wParam,
            [IntPtr]$lParam,
            [ref]$handled
        )

        if ($msg -eq [TomatoPowerSetting]::WM_POWERBROADCAST -and $wParam.ToInt32() -eq [TomatoPowerSetting]::PBT_POWERSETTINGCHANGE) {
            $settingGuid = [TomatoPowerSetting]::ReadPowerSettingGuid($lParam)
            if ($settingGuid -eq [TomatoPowerSetting]::GUID_CONSOLE_DISPLAY_STATE) {
                $displayState = [TomatoPowerSetting]::ReadPowerSettingValue($lParam)
                if ($displayState -eq 0) {
                    Reset-Timer "Display off detected. Reset to zero."
                }
            }
        }

        return [IntPtr]::Zero
    })

    $windowHandle = (New-Object Windows.Interop.WindowInteropHelper($window)).Handle
    $displayGuid = [TomatoPowerSetting]::GUID_CONSOLE_DISPLAY_STATE
    $script:powerNotificationHandle = [TomatoPowerSetting]::RegisterPowerSettingNotification(
        $windowHandle,
        [ref]$displayGuid,
        [TomatoPowerSetting]::DEVICE_NOTIFY_WINDOW_HANDLE
    )

    $script:powerModeHandler = [Microsoft.Win32.PowerModeChangedEventHandler]{
        param($sender, $eventArgs)
        if ($eventArgs.Mode -eq [Microsoft.Win32.PowerModes]::Suspend -or $eventArgs.Mode -eq [Microsoft.Win32.PowerModes]::Resume) {
            $window.Dispatcher.Invoke([Action]{ Reset-Timer "Sleep or resume detected. Reset to zero." })
        }
    }

    $script:sessionSwitchHandler = [Microsoft.Win32.SessionSwitchEventHandler]{
        param($sender, $eventArgs)
        if ($eventArgs.Reason -eq [Microsoft.Win32.SessionSwitchReason]::SessionLock -or $eventArgs.Reason -eq [Microsoft.Win32.SessionSwitchReason]::SessionUnlock) {
            $window.Dispatcher.Invoke([Action]{ Reset-Timer "Lock or unlock detected. Reset to zero." })
        }
    }

    [Microsoft.Win32.SystemEvents]::add_PowerModeChanged($script:powerModeHandler)
    [Microsoft.Win32.SystemEvents]::add_SessionSwitch($script:sessionSwitchHandler)
    $timer.Start()
    Update-Display
})

$window.Add_Closing({
    $timer.Stop()
    if ($script:powerModeHandler) {
        [Microsoft.Win32.SystemEvents]::remove_PowerModeChanged($script:powerModeHandler)
    }
    if ($script:sessionSwitchHandler) {
        [Microsoft.Win32.SystemEvents]::remove_SessionSwitch($script:sessionSwitchHandler)
    }
    if ($script:powerNotificationHandle -ne [IntPtr]::Zero) {
        [void][TomatoPowerSetting]::UnregisterPowerSettingNotification($script:powerNotificationHandle)
    }
    if ($script:notifyIcon) {
        $script:notifyIcon.Visible = $false
        $script:notifyIcon.Dispose()
    }
})

try {
    $script:notifyIcon = [System.Windows.Forms.NotifyIcon]::new()
    $script:notifyIcon.Text = "Tomato Clock"
    $script:notifyIcon.Icon = [System.Drawing.SystemIcons]::Application
    $script:notifyIcon.Visible = $true
    $script:notifyIcon.Add_DoubleClick({
        $window.Show()
        $window.WindowState = [Windows.WindowState]::Normal
        $window.Activate()
    })
} catch {
    $script:notifyIcon = $null
}

[void]$window.ShowDialog()
