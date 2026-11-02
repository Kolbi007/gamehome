# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (c) 2026 The GameHome contributors
<#
.SYNOPSIS
    Rent skrivebord på gaming-PC-en mens et spill strømmes: skjuler skrivebordsikoner og oppgavelinjen, og setter dem
    tilbake etterpå.

.DESCRIPTION
    Brukes som global prep-kommando i Vibepollo (kjører som den påloggede brukeren, ikke som administrator):
        do:   powershell -NoProfile -ExecutionPolicy Bypass -File "<sti>\Set-StreamDesktop.ps1" -Action Hide
        undo: powershell -NoProfile -ExecutionPolicy Bypass -File "<sti>\Set-StreamDesktop.ps1" -Action Restore
    Sikkerhetsnett ved pålogging: samme skript med -Action Restore.

    Endringene skjer uten å starte Utforsker på nytt: ikonene skjules med samme kommando som «Vis skrivebordsikoner» i
    Utforsker, og oppgavelinjen settes til automatisk skjult med SHAppBarMessage. Forrige tilstand lagres i
    %LOCALAPPDATA%\GameHome\stream-desktop.json, så Restore bare setter tilbake det Hide endret.

    Hide åpner også et dekkbilde: et rolig, mørkt fullskjermsbilde over skrivebordet og åpne vinduer. Spillet åpnes
    over det, og Restore lukker det. Bakgrunnsbildet (også lysbildefremvisning) røres ikke.

    Skriptet avslutter alltid med kode 0. Vibepollo avbryter spillstarten hvis en prep-kommando feiler, og et rent
    skrivebord er ikke viktigere enn at spillet starter.
#>
param(
    [ValidateSet('Hide', 'Restore', 'Status', 'Cover')]
    [string] $Action = 'Status'
)

$ErrorActionPreference = 'Stop'
$stateDir = Join-Path $env:LOCALAPPDATA 'GameHome'
$stateFile = Join-Path $stateDir 'stream-desktop.json'
$coverPidFile = Join-Path $stateDir 'stream-cover.pid'
$logFile = Join-Path $stateDir 'stream-desktop.log'

function Write-Log([string] $message) {
    try {
        New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
        Add-Content -Path $logFile -Value ("{0:yyyy-MM-dd HH:mm:ss} {1} {2}" -f (Get-Date), $Action, $message) -Encoding UTF8
    } catch { }
}

# Dekkbildet kjører i en egen, skjult PowerShell-prosess, så prep-kommandoen kan returnere med en gang.
if ($Action -eq 'Cover') {
    try {
        Add-Type -AssemblyName System.Windows.Forms, System.Drawing
        Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

public class GameHomeCover : Form
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    // Tegn i skjermens faktiske oppløsning. Uten dette forstørrer Windows bildet ved skalering (for eksempel 150 %),
    // og det ser grovt ut. -4 = DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2.
    public static void UseNativeResolution()
    {
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
    }

    public GameHomeCover()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(11, 13, 16);
        DoubleBuffered = true;
        Text = "GameHome";
        Fit();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) => BeginInvoke((Action)Fit);
    }

    // Virtuell skjerm kan komme etter at bildet er åpnet; da følger bildet primærskjermen.
    private void Fit()
    {
        Bounds = Screen.PrimaryScreen.Bounds;
        Invalidate();
    }

    protected override bool ShowWithoutActivation { get { return false; } }

    // Musepekeren skjules over dekkbildet; spillet viser den igjen når det tar over.
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Cursor.Hide();
        Activate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = ClientRectangle;
        if (r.Width == 0 || r.Height == 0) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        // Dyp, mørk bunn fra nesten svart til midnattsblå.
        using (var bg = new LinearGradientBrush(r, Color.FromArgb(8, 10, 14), Color.FromArgb(14, 20, 34), LinearGradientMode.Vertical))
            g.FillRectangle(bg, r);

        // Myk glød litt over midten, som et svakt lys i rommet.
        Glow(g, r.Width * 0.5f, r.Height * 0.42f, r.Width * 0.55f, r.Height * 0.55f, Color.FromArgb(70, 40, 90, 150));
        Glow(g, r.Width * 0.72f, r.Height * 0.70f, r.Width * 0.35f, r.Height * 0.35f, Color.FromArgb(40, 90, 50, 140));

        // Tynn horisontlinje som gir dybde.
        using (var line = new LinearGradientBrush(new Rectangle(0, 0, r.Width, 2), Color.Transparent, Color.Transparent, LinearGradientMode.Horizontal))
        {
            var blend = new ColorBlend
            {
                Colors = new[] { Color.FromArgb(0, 120, 170, 255), Color.FromArgb(90, 120, 170, 255), Color.FromArgb(0, 120, 170, 255) },
                Positions = new[] { 0f, 0.5f, 1f },
            };
            line.InterpolationColors = blend;
            g.FillRectangle(line, 0, (int)(r.Height * 0.62), r.Width, Math.Max(1, r.Height / 720));
        }

        var scale = r.Height / 1080f;
        using (var title = new Font("Segoe UI Light", 44 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var sub = new Font("Segoe UI", 20 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var titleBrush = new SolidBrush(Color.FromArgb(225, 232, 240, 250)))
        using (var subBrush = new SolidBrush(Color.FromArgb(150, 160, 175, 195)))
        {
            var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("GameHome", title, titleBrush, new RectangleF(0, r.Height * 0.52f, r.Width, 60 * scale), center);
            g.DrawString("Starter spillet …", sub, subBrush, new RectangleF(0, r.Height * 0.52f + 62 * scale, r.Width, 32 * scale), center);
        }
    }

    private static void Glow(Graphics g, float cx, float cy, float w, float h, Color color)
    {
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(cx - w / 2, cy - h / 2, w, h);
            using (var brush = new PathGradientBrush(path))
            {
                brush.CenterColor = color;
                brush.SurroundColors = new[] { Color.FromArgb(0, color) };
                g.FillPath(brush, path);
            }
        }
    }
}
'@
        [GameHomeCover]::UseNativeResolution()
        $form = New-Object GameHomeCover
        [System.Windows.Forms.Application]::Run($form)
    } catch {
        Write-Log "dekkbilde feilet: $($_.Exception.Message)"
    }
    exit 0
}

function Start-Cover {
    if (Test-Path $coverPidFile) {
        $old = Get-Process -Id ([int](Get-Content $coverPidFile -Raw)) -ErrorAction SilentlyContinue
        if ($old) { return }
    }
    $p = Start-Process -FilePath "$PSHOME\powershell.exe" -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Action', 'Cover')
    Set-Content -Path $coverPidFile -Value $p.Id
}

function Stop-Cover {
    if (-not (Test-Path $coverPidFile)) { return }
    $p = Get-Process -Id ([int](Get-Content $coverPidFile -Raw)) -ErrorAction SilentlyContinue
    # Bare vår egen PowerShell-prosess; et gjenbrukt PID etter omstart tilhører noe annet.
    if ($p -and $p.ProcessName -eq 'powershell') { Stop-Process -Id $p.Id -Force }
    Remove-Item -Path $coverPidFile -Force
}

try {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class GameHomeDesktop
{
    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    private const uint ABM_GETSTATE = 4;
    private const uint ABM_SETSTATE = 10;
    private const int ABS_AUTOHIDE = 1;
    private const int ABS_ALWAYSONTOP = 2;
    private const uint WM_COMMAND = 0x0111;
    private const int TOGGLE_DESKTOP_ICONS = 0x7402;

    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(uint message, ref APPBARDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    public static bool TaskbarAutoHide
    {
        get
        {
            var data = new APPBARDATA { cbSize = Marshal.SizeOf(typeof(APPBARDATA)) };
            return ((long)SHAppBarMessage(ABM_GETSTATE, ref data) & ABS_AUTOHIDE) != 0;
        }
        set
        {
            var data = new APPBARDATA
            {
                cbSize = Marshal.SizeOf(typeof(APPBARDATA)),
                hWnd = FindWindow("Shell_TrayWnd", null),
                lParam = (IntPtr)(value ? ABS_AUTOHIDE : ABS_ALWAYSONTOP),
            };
            SHAppBarMessage(ABM_SETSTATE, ref data);
        }
    }

    // Skrivebordsvinduet ligger under Progman eller, når bakgrunnen har byttet, under en WorkerW.
    private static IntPtr DefView()
    {
        var view = FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
        var worker = IntPtr.Zero;
        while (view == IntPtr.Zero)
        {
            worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null);
            if (worker == IntPtr.Zero) break;
            view = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
        }
        return view;
    }

    public static bool? IconsVisible
    {
        get
        {
            var view = DefView();
            if (view == IntPtr.Zero) return null;
            var list = FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
            return list != IntPtr.Zero && IsWindowVisible(list);
        }
    }

    public static bool ToggleIcons()
    {
        var view = DefView();
        if (view == IntPtr.Zero) return false;
        SendMessage(view, WM_COMMAND, (IntPtr)TOGGLE_DESKTOP_ICONS, IntPtr.Zero);
        return true;
    }
}
'@ -ErrorAction SilentlyContinue

    $icons = [GameHomeDesktop]::IconsVisible
    $autoHide = [GameHomeDesktop]::TaskbarAutoHide

    switch ($Action) {
        'Status' {
            "Ikoner synlige: $icons, oppgavelinje automatisk skjult: $autoHide, lagret tilstand: $(Test-Path $stateFile)"
        }
        'Hide' {
            # Lagre bare første gang, så en ny Hide (for eksempel etter gjenoppkobling) ikke overskriver originalen.
            $firstHide = -not (Test-Path $stateFile)
            if ($firstHide) {
                New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
                @{ IconsVisible = $icons; TaskbarAutoHide = $autoHide; MinimizedAll = $true } | ConvertTo-Json | Set-Content -Path $stateFile -Encoding UTF8

                # Som Win+D: åpne vinduer minimeres, så streamen bare viser bakgrunnen og så spillet.
                (New-Object -ComObject Shell.Application).MinimizeAll()
            }
            if ($icons -eq $true) { [void][GameHomeDesktop]::ToggleIcons() }
            if (-not $autoHide) { [GameHomeDesktop]::TaskbarAutoHide = $true }
            Start-Cover
            Write-Log "ikoner var $icons, autoskjul var $autoHide, minimerte vinduer: $firstHide, dekkbilde åpnet"
        }
        'Restore' {
            Stop-Cover
            if (-not (Test-Path $stateFile)) {
                Write-Log 'ingen lagret tilstand; ingenting å sette tilbake'
                break
            }
            $saved = Get-Content -Path $stateFile -Raw | ConvertFrom-Json
            if ($saved.IconsVisible -eq $true -and [GameHomeDesktop]::IconsVisible -eq $false) { [void][GameHomeDesktop]::ToggleIcons() }
            if ($saved.TaskbarAutoHide -eq $false -and [GameHomeDesktop]::TaskbarAutoHide) { [GameHomeDesktop]::TaskbarAutoHide = $false }
            if ($saved.MinimizedAll -eq $true) { (New-Object -ComObject Shell.Application).UndoMinimizeALL() }
            Remove-Item -Path $stateFile -Force
            Write-Log "satte tilbake ikoner=$($saved.IconsVisible), autoskjul=$($saved.TaskbarAutoHide), vinduer=$($saved.MinimizedAll)"
        }
    }
} catch {
    Write-Log "feil: $($_.Exception.Message)"
}

exit 0
