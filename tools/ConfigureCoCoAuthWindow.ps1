param([switch]$CheckOnly, [string]$StatusPath)
$ErrorActionPreference = 'Stop'

$repoPath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repoPath 'src\CoCoReTrade.Web\CoCoReTrade.Web.csproj'
if (!(Test-Path -LiteralPath $projectPath -PathType Leaf)) { throw 'Không tìm thấy dự án CoCo.' }
[xml]$projectDocument = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
$secretIdNode = $projectDocument.SelectSingleNode('//UserSecretsId')
if (!$secretIdNode -or [string]::IsNullOrWhiteSpace($secretIdNode.InnerText)) {
    throw 'Dự án chưa có UserSecretsId. Chưa thể lưu cấu hình.'
}
$dotnetPath = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
if ($CheckOnly) { Write-Output 'Sẵn sàng mở cửa sổ cấu hình CoCo. Chưa đọc hoặc ghi khóa.'; return }

$script:lastSavedSection = $null
$script:lastSaveSucceeded = $null
function Write-CoCoWindowStatus([string]$Event, [string]$Section, [Nullable[bool]]$Succeeded) {
    if ([string]::IsNullOrWhiteSpace($StatusPath)) { return }
    if ($Event -notin @('Shown', 'Closed', 'Saved')) { return }
    if ($Event -eq 'Saved') {
        $script:lastSavedSection = if ($Section -in @('OTP', 'Google')) { $Section } else { $null }
        $script:lastSaveSucceeded = [bool]$Succeeded
    }
    try {
        # This file contains only fixed event names and booleans. Never include textbox values.
        $payload = [ordered]@{
            event = $Event
            processId = [int]$PID
            windowShown = ($Event -ne 'Closed')
            section = $script:lastSavedSection
            success = $script:lastSaveSucceeded
        } | ConvertTo-Json -Compress
        $statusFile = [System.IO.Path]::GetFullPath($StatusPath)
        $statusDirectory = [System.IO.Path]::GetDirectoryName($statusFile)
        if (![System.IO.Directory]::Exists($statusDirectory)) { [void][System.IO.Directory]::CreateDirectory($statusDirectory) }
        [System.IO.File]::WriteAllText($statusFile, $payload, (New-Object System.Text.UTF8Encoding($false)))
    }
    catch { } # A status-file failure must not expose CLI output or interrupt private entry.
}

function Save-CoCoUserSecrets([hashtable]$Settings) {
    $process = New-Object System.Diagnostics.Process
    $json = $null
    $inputBytes = $null
    try {
        $start = New-Object System.Diagnostics.ProcessStartInfo
        $start.FileName = $dotnetPath
        # Only the known project path enters arguments. Secret values travel through stdin.
        $start.Arguments = 'user-secrets set --project "' + $projectPath + '"'
        $start.WorkingDirectory = $repoPath
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardInput = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $start.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
        $start.StandardErrorEncoding = New-Object System.Text.UTF8Encoding($false)
        $start.EnvironmentVariables['DOTNET_NOLOGO'] = '1'
        $process.StartInfo = $start
        if (!$process.Start()) { return $false }
        # Drain both streams privately. Never display CLI output, including error text.
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $json = $Settings | ConvertTo-Json -Compress
        # Write UTF-8 directly: .NET Framework used by PowerShell 5.1 has no
        # ProcessStartInfo.StandardInputEncoding property.
        $inputBytes = [System.Text.Encoding]::UTF8.GetBytes($json + [Environment]::NewLine)
        $inputStream = $process.StandardInput.BaseStream
        $inputStream.Write($inputBytes, 0, $inputBytes.Length)
        $inputStream.Flush()
        $inputStream.Close()
        if (!$process.WaitForExit(30000)) { $process.Kill(); return $false }
        $process.WaitForExit()
        return ($process.ExitCode -eq 0)
    }
    catch { return $false }
    finally {
        $json = $null
        if ($null -ne $inputBytes) { [Array]::Clear($inputBytes, 0, $inputBytes.Length) }
        $stdoutTask = $null
        $stderrTask = $null
        $Settings.Clear()
        $process.Dispose()
    }
}

[System.Windows.Forms.Application]::EnableVisualStyles()
$form = New-Object System.Windows.Forms.Form
$form.Text = 'CoCo ReTrade | Cấu hình OTP và Google'
$form.ClientSize = New-Object System.Drawing.Size(730, 760)
$form.MinimumSize = New-Object System.Drawing.Size(660, 620)
$form.StartPosition = 'CenterScreen'
$form.ShowInTaskbar = $true
$form.WindowState = [System.Windows.Forms.FormWindowState]::Normal
$form.Font = New-Object System.Drawing.Font('Segoe UI', 10)
$form.BackColor = [System.Drawing.Color]::White
$scroll = New-Object System.Windows.Forms.Panel
$scroll.Dock = 'Fill'
$scroll.AutoScroll = $true
$form.Controls.Add($scroll)
$layout = New-Object System.Windows.Forms.TableLayoutPanel
$layout.Dock = 'Top'
$layout.AutoSize = $true
$layout.ColumnCount = 1
$layout.Padding = New-Object System.Windows.Forms.Padding(20)
$layout.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle('Percent', 100))) | Out-Null
$scroll.Controls.Add($layout)

function New-CoCoLabel([string]$Text, [int]$Height) {
    $label = New-Object System.Windows.Forms.Label
    $label.Text = $Text
    $label.Dock = 'Top'
    $label.Height = $Height
    $label.Margin = New-Object System.Windows.Forms.Padding(0, 0, 0, 10)
    return $label
}
function New-CoCoTextBox([bool]$Private = $false) {
    $box = New-Object System.Windows.Forms.TextBox
    $box.Dock = 'Top'
    $box.UseSystemPasswordChar = $Private
    $box.Margin = New-Object System.Windows.Forms.Padding(0, 0, 0, 10)
    return $box
}
function New-CoCoGroup([string]$Title) {
    $group = New-Object System.Windows.Forms.GroupBox
    $group.Text = $Title
    $group.Dock = 'Top'
    $group.AutoSize = $true
    $group.Padding = New-Object System.Windows.Forms.Padding(14, 24, 14, 12)
    $group.Margin = New-Object System.Windows.Forms.Padding(0, 4, 0, 16)
    $fields = New-Object System.Windows.Forms.TableLayoutPanel
    $fields.Dock = 'Top'
    $fields.AutoSize = $true
    $fields.ColumnCount = 1
    $fields.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle('Percent', 100))) | Out-Null
    $group.Controls.Add($fields)
    return @{ Group = $group; Fields = $fields }
}
function Show-CoCoSaveResult([bool]$Succeeded, [string]$Section) {
    Write-CoCoWindowStatus 'Saved' $Section $Succeeded
    if ($Succeeded) {
        $status.Text = "Đã lưu cấu hình $Section trên máy. Chưa gửi email, chưa khởi động lại website."
        $status.ForeColor = [System.Drawing.Color]::Black
    }
    else {
        $status.Text = 'Chưa lưu được cấu hình. Kiểm tra .NET và quyền ghi User Secrets rồi thử lại.'
        $status.ForeColor = [System.Drawing.Color]::FromArgb(248, 55, 88)
    }
}

$layout.Controls.Add((New-CoCoLabel 'Nhập khóa trực tiếp tại đây, không gửi qua chat. Mỗi nút chỉ lưu phần tương ứng; mục để trống không xóa cấu hình đã có. Khóa lưu ngoài mã nguồn bằng ASP.NET User Secrets.' 65))
$smtp = New-CoCoGroup '1. Gmail gửi mã OTP'
$smtp.Fields.Controls.Add((New-CoCoLabel 'Gmail: https://myaccount.google.com/apppasswords  (cần bật xác minh hai bước tại https://myaccount.google.com/security)' 50))
$smtp.Fields.Controls.Add((New-CoCoLabel 'Địa chỉ Gmail gửi OTP' 25))
$emailBox = New-CoCoTextBox
$emailBox.Text = 'cocoretradekygui@gmail.com'
$smtp.Fields.Controls.Add($emailBox)
$smtp.Fields.Controls.Add((New-CoCoLabel 'Gmail App Password (không nhập mật khẩu đăng nhập Gmail)' 25))
$smtp.Fields.Controls.Add((New-CoCoLabel 'Mật khẩu đã gửi qua chat cần thu hồi và tạo lại trước khi nhập.' 42))
$smtpPassword = New-CoCoTextBox $true
$smtp.Fields.Controls.Add($smtpPassword)
$saveSmtp = New-Object System.Windows.Forms.Button
$saveSmtp.Text = 'Lưu cấu hình OTP'
$saveSmtp.AutoSize = $true
$saveSmtp.Height = 36
$smtp.Fields.Controls.Add($saveSmtp)
$layout.Controls.Add($smtp.Group)

$google = New-CoCoGroup '2. Đăng nhập Google'
$google.Fields.Controls.Add((New-CoCoLabel 'Tạo OAuth Client loại Web application tại https://console.cloud.google.com/. Dùng các Redirect URI dưới đây khi cấu hình trên máy.' 48))
$callbacks = New-CoCoTextBox
$callbacks.Multiline = $true
$callbacks.ReadOnly = $true
$callbacks.Height = 58
$callbacks.Text = "http://localhost:5001/signin-google`r`nhttp://localhost:5002/signin-google"
$google.Fields.Controls.Add($callbacks)
$google.Fields.Controls.Add((New-CoCoLabel 'Google OAuth Client ID' 25))
$clientIdBox = New-CoCoTextBox
$google.Fields.Controls.Add($clientIdBox)
$google.Fields.Controls.Add((New-CoCoLabel 'Google OAuth Client Secret' 25))
$clientSecretBox = New-CoCoTextBox $true
$google.Fields.Controls.Add($clientSecretBox)
$saveGoogle = New-Object System.Windows.Forms.Button
$saveGoogle.Text = 'Lưu cấu hình Google'
$saveGoogle.AutoSize = $true
$saveGoogle.Height = 36
$google.Fields.Controls.Add($saveGoogle)
$layout.Controls.Add($google.Group)
$status = New-CoCoLabel 'Lưu xong, quay lại cuộc trò chuyện để khởi động lại website và kiểm tra. Công cụ này không mở trình duyệt hoặc gửi email.' 60
$layout.Controls.Add($status)

$focusTimer = New-Object System.Windows.Forms.Timer
$focusTimer.Interval = 3500
$focusTimer.Add_Tick({
    $form.TopMost = $false
    $focusTimer.Stop()
})
$form.Add_Shown({
    $form.WindowState = [System.Windows.Forms.FormWindowState]::Normal
    $form.TopMost = $true
    $form.BringToFront()
    $form.Activate()
    $scroll.ScrollControlIntoView($smtpPassword)
    $form.ActiveControl = $smtpPassword
    [void]$smtpPassword.Focus()
    Write-CoCoWindowStatus 'Shown'
    $focusTimer.Start()
})

$saveSmtp.Add_Click({
    $senderEmail = $emailBox.Text.Trim()
    $appPassword = $smtpPassword.Text.Replace(' ', '').Trim()
    if ($senderEmail -notmatch '^[^\s@]+@[^\s@]+\.[^\s@]+$' -or [string]::IsNullOrWhiteSpace($appPassword)) {
        $status.Text = 'Nhập Gmail hợp lệ và Gmail App Password để lưu OTP. Chưa thay đổi cấu hình.'
        $appPassword = $null
        return
    }
    $saveSmtp.Enabled = $false
    try {
        $saved = Save-CoCoUserSecrets @{
            'Smtp:Host' = 'smtp.gmail.com'; 'Smtp:Port' = '587'
            'Smtp:From' = $senderEmail; 'Smtp:User' = $senderEmail; 'Smtp:Password' = $appPassword
        }
        Show-CoCoSaveResult $saved 'OTP'
        if ($saved) { $smtpPassword.Clear() }
    }
    finally { $appPassword = $null; $senderEmail = $null; $saveSmtp.Enabled = $true }
})
$saveGoogle.Add_Click({
    $clientId = $clientIdBox.Text.Trim()
    $clientSecret = $clientSecretBox.Text.Trim()
    if ([string]::IsNullOrWhiteSpace($clientId) -or [string]::IsNullOrWhiteSpace($clientSecret)) {
        $status.Text = 'Nhập đủ Google Client ID và Client Secret để lưu Google. Chưa thay đổi cấu hình.'
        $clientId = $null; $clientSecret = $null
        return
    }
    $saveGoogle.Enabled = $false
    try {
        $saved = Save-CoCoUserSecrets @{ 'Authentication:Google:ClientId' = $clientId; 'Authentication:Google:ClientSecret' = $clientSecret }
        Show-CoCoSaveResult $saved 'Google'
        if ($saved) { $clientSecretBox.Clear() }
    }
    finally { $clientId = $null; $clientSecret = $null; $saveGoogle.Enabled = $true }
})
$form.Add_FormClosing({
    param($sender, $eventArgs)
    if ($smtpPassword.TextLength -gt 0 -or $clientSecretBox.TextLength -gt 0) {
        $choice = [System.Windows.Forms.MessageBox]::Show($form,
            'Bạn còn khóa chưa lưu. Đóng cửa sổ sẽ bỏ phần vừa nhập. Vẫn đóng cửa sổ?',
            'CoCo ReTrade', [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Warning, [System.Windows.Forms.MessageBoxDefaultButton]::Button2)
        if ($choice -ne [System.Windows.Forms.DialogResult]::Yes) { $eventArgs.Cancel = $true }
    }
})
$form.Add_FormClosed({
    $focusTimer.Stop()
    foreach ($field in @($emailBox, $smtpPassword, $clientIdBox, $clientSecretBox, $callbacks)) { $field.Clear() }
    Write-CoCoWindowStatus 'Closed'
})
try { [void]$form.ShowDialog() }
finally { $focusTimer.Dispose(); $form.Dispose() }
