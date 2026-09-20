# Grava o microfone padrao do Windows em WAV 16 kHz mono ate receber uma linha no stdin
# (ou ate -MaxSeconds). Usa MCI (winmm), entao nao precisa instalar nada no Windows.
param(
    [Parameter(Mandatory = $true)][string]$OutFile,
    [int]$MaxSeconds = 120
)

Add-Type -Namespace VoiceEn -Name Mci -MemberDefinition @'
[DllImport("winmm.dll", CharSet = CharSet.Unicode)]
public static extern int mciSendString(string command, System.Text.StringBuilder ret, int retLen, IntPtr callback);
[DllImport("winmm.dll", CharSet = CharSet.Unicode)]
public static extern bool mciGetErrorString(int err, System.Text.StringBuilder text, int len);
'@

function Send-Mci([string]$cmd) {
    $err = [VoiceEn.Mci]::mciSendString($cmd, $null, 0, [IntPtr]::Zero)
    if ($err -ne 0) {
        $msg = New-Object System.Text.StringBuilder 256
        [void][VoiceEn.Mci]::mciGetErrorString($err, $msg, 256)
        [Console]::Error.WriteLine("MCI '$cmd': $msg")
        exit 1
    }
}

Send-Mci 'open new type waveaudio alias rec'
Send-Mci 'set rec time format ms bitspersample 16 channels 1 samplespersec 16000 bytespersec 32000 alignment 2'
Send-Mci 'record rec'
[Console]::Out.WriteLine('[voice-en] GRAVANDO - fale em portugues e aperte Enter para terminar')

$reader = [Console]::In.ReadLineAsync()
[void]$reader.Wait($MaxSeconds * 1000)

Send-Mci 'stop rec'
Send-Mci "save rec `"$OutFile`""
Send-Mci 'close rec'
