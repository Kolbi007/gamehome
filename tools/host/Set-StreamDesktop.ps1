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
    [string] $Action = 'Status',

    # Intern: Hide er startet i bakgrunnen og skal gjøre jobben nå.
    [switch] $Now
)

$ErrorActionPreference = 'Stop'
$stateDir = Join-Path $env:LOCALAPPDATA 'GameHome'
$stateFile = Join-Path $stateDir 'stream-desktop.json'
$coverPidFile = Join-Path $stateDir 'stream-cover.pid'
$hidePidFile = Join-Path $stateDir 'stream-hide.pid'
$logFile = Join-Path $stateDir 'stream-desktop.log'

function Write-Log([string] $message) {
    try {
        New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
        Add-Content -Path $logFile -Value ("{0:yyyy-MM-dd HH:mm:ss} {1} {2}" -f (Get-Date), $Action, $message) -Encoding UTF8
    } catch { }
}

# Hjelpekoden (C#) kompileres én gang og lagres som DLL ved siden av skriptet; senere kjøringer laster den på et øyeblikk.
# Navnet inneholder en hash av kildekoden, så en endret kildekode gir en ny DLL.
function Import-Cached([string] $name, [string] $source, [string[]] $references) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $hash = -join ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($source))[0..7] | ForEach-Object { $_.ToString('x2') })
    $cacheDir = Join-Path $stateDir 'cache'
    $dll = Join-Path $cacheDir "$name-$hash.dll"
    if (-not (Test-Path $dll)) {
        New-Item -ItemType Directory -Force -Path $cacheDir | Out-Null
        $temp = "$dll.$PID.tmp"
        if ($references.Count -gt 0) {
            Add-Type -TypeDefinition $source -ReferencedAssemblies $references -OutputAssembly $temp -OutputType Library
        } else {
            Add-Type -TypeDefinition $source -OutputAssembly $temp -OutputType Library
        }
        Move-Item -Path $temp -Destination $dll -Force
    }
    Add-Type -Path $dll
}

# Hide kjøres i bakgrunnen, så Vibepollo ikke venter på skriptet (målt 2,7 s) før spillet startes. Dekkbildet startes
# først av alt, så det er oppe før streamen begynner og skjuler vinduer som ikke kan minimeres.
if ($Action -eq 'Hide' -and -not $Now) {
    try {
        New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
        $cover = Start-Process -FilePath "$PSHOME\powershell.exe" -WindowStyle Hidden -PassThru -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Action', 'Cover')
        Set-Content -Path $coverPidFile -Value $cover.Id
        $p = Start-Process -FilePath "$PSHOME\powershell.exe" -WindowStyle Hidden -PassThru -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Action', 'Hide', '-Now')
        Set-Content -Path $hidePidFile -Value $p.Id
    } catch {
        Write-Log "kunne ikke starte i bakgrunnen: $($_.Exception.Message)"
    }
    exit 0
}

# Restore venter på en Hide som fortsatt jobber (spillet avsluttet svært raskt), så ingenting blir liggende skjult.
if ($Action -eq 'Restore' -and (Test-Path $hidePidFile)) {
    try {
        $hide = Get-Process -Id ([int](Get-Content $hidePidFile -Raw)) -ErrorAction SilentlyContinue
        if ($hide -and $hide.ProcessName -eq 'powershell') { [void]$hide.WaitForExit(10000) }
    } catch { }
}

# Dekkbildet kjører i en egen, skjult PowerShell-prosess, så prep-kommandoen kan returnere med en gang.
if ($Action -eq 'Cover') {
    try {
        Add-Type -AssemblyName System.Windows.Forms, System.Drawing
        $coverSource = @'
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

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    // Explorer viser oppgavelinjen igjen når et nytt vindu (dette) får forgrunnen; skjul den på nytt.
    private static void HideTaskbar()
    {
        foreach (var name in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
        {
            var window = IntPtr.Zero;
            while ((window = FindWindowEx(IntPtr.Zero, window, name, null)) != IntPtr.Zero)
            {
                ShowWindow(window, 0);
            }
        }
    }

    protected override bool ShowWithoutActivation { get { return false; } }

    // Musepekeren skjules over dekkbildet; spillet viser den igjen når det tar over.
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Cursor.Hide();
        Activate();
        HideTaskbar();

        // Lysbølgen: én runde på 2,6 s, 30 bilder i sekundet. Tegner bare linjens felt på nytt.
        _animation.Interval = 33;
        _animation.Tick += (s, a) =>
        {
            _sweep += 1.6f / (2.6f * 30f);
            if (_sweep > 1.3f) _sweep = -0.3f;
            var r = ClientRectangle;
            Invalidate(new Rectangle(0, (int)(r.Height * 0.62) - 8, r.Width, 16 + r.Height / 270));
        };
        _animation.Start();

        // Explorer kan vise oppgavelinjen igjen et øyeblikk etter at et vindu har skiftet; skjul den en gang til.
        var again = new Timer { Interval = 1500 };
        again.Tick += (s, a) => { again.Stop(); HideTaskbar(); };
        again.Start();
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

        // Lysbølge som glir langs linjen fra venstre mot høyre mens vi venter på spillet.
        var lineY = (int)(r.Height * 0.62);
        var thickness = Math.Max(2, r.Height / 540);
        var sweepX = _sweep * r.Width;
        var half = r.Width * 0.12f;
        var band = new RectangleF(sweepX - half, lineY - thickness / 2f, half * 2, thickness * 2);
        if (band.Right > 0 && band.Left < r.Width)
        {
            using (var glow = new LinearGradientBrush(band, Color.Transparent, Color.Transparent, LinearGradientMode.Horizontal))
            {
                glow.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { Color.FromArgb(0, 170, 210, 255), Color.FromArgb(220, 200, 230, 255), Color.FromArgb(0, 170, 210, 255) },
                    Positions = new[] { 0f, 0.5f, 1f },
                };
                g.FillRectangle(glow, band);
            }
        }

        // Spillets navn stort over horisontlinjen, «Starter …» under, og et lite GameHome-merke nederst.
        var scale = r.Height / 1080f;
        var hasGame = !string.IsNullOrWhiteSpace(GameTitle);
        using (var title = new Font("Segoe UI Light", (hasGame ? 64 : 44) * scale, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var sub = new Font("Segoe UI", 22 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var mark = new Font("Segoe UI Semibold", 16 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var titleBrush = new SolidBrush(Color.FromArgb(235, 236, 242, 252)))
        using (var subBrush = new SolidBrush(Color.FromArgb(160, 160, 175, 195)))
        using (var markBrush = new SolidBrush(Color.FromArgb(90, 160, 175, 195)))
        {
            var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
            var top = r.Height * 0.62f - 150 * scale;
            g.DrawString(hasGame ? GameTitle : "GameHome", title, titleBrush, new RectangleF(r.Width * 0.08f, top, r.Width * 0.84f, 110 * scale), center);
            g.DrawString("Starter …", sub, subBrush, new RectangleF(0, r.Height * 0.62f + 24 * scale, r.Width, 36 * scale), center);
            if (hasGame)
            {
                g.DrawString("GAMEHOME", mark, markBrush, new RectangleF(0, r.Height - 90 * scale, r.Width, 30 * scale), center);
            }
        }
    }

    /// <summary>Navnet på spillet, fra Vibepollos SUNSHINE_APP_NAME.</summary>
    public static string GameTitle;

    private readonly Timer _animation = new Timer();
    private float _sweep = -0.3f;

    // Animasjonen går bare mens dekkbildet er i forgrunnen; når spillet tar over, stopper den og bruker ingen kraft.
    protected override void OnActivated(EventArgs e) { base.OnActivated(e); if (_animation.Interval > 0 && Visible) _animation.Start(); }

    protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); _animation.Stop(); }

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
        Import-Cached 'cover' $coverSource @('System.Windows.Forms', 'System.Drawing')
        [GameHomeCover]::UseNativeResolution()
        # Vibepollo gir prep-kommandoen navnet på appen; prosessen for dekkbildet arver miljøet.
        [GameHomeCover]::GameTitle = $env:SUNSHINE_APP_NAME
        if (-not [GameHomeCover]::GameTitle) { [GameHomeCover]::GameTitle = $env:APOLLO_APP_NAME }
        $form = New-Object GameHomeCover
        $form.add_Shown({ Write-Log 'dekkbilde vist' })
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
    $desktopSource = @'
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

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    /// <summary>
    /// Skjuler eller viser oppgavelinjen helt. Automatisk skjult oppgavelinje spretter fram når et nytt program åpner et
    /// vindu (målt 2026-11-02: synlig et øyeblikk da spillet startet); en skjult oppgavelinje gjør ikke det.
    /// </summary>
    public static void SetTaskbarVisible(bool visible)
    {
        foreach (var name in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
        {
            var window = IntPtr.Zero;
            while ((window = FindWindowEx(IntPtr.Zero, window, name, null)) != IntPtr.Zero)
            {
                ShowWindow(window, visible ? 5 : 0); // SW_SHOW / SW_HIDE
            }
        }
    }

    public static bool TaskbarVisible
    {
        get { var tray = FindWindow("Shell_TrayWnd", null); return tray != IntPtr.Zero && IsWindowVisible(tray); }
    }
}
'@
    Import-Cached 'desktop' $desktopSource @()

    $icons = [GameHomeDesktop]::IconsVisible
    $autoHide = [GameHomeDesktop]::TaskbarAutoHide

    switch ($Action) {
        'Status' {
            "Ikoner synlige: $icons, oppgavelinje synlig: $([GameHomeDesktop]::TaskbarVisible), automatisk skjult: $autoHide, lagret tilstand: $(Test-Path $stateFile)"
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
            [GameHomeDesktop]::SetTaskbarVisible($false)
            Start-Cover
            Write-Log "ikoner var $icons, autoskjul var $autoHide, minimerte vinduer: $firstHide, dekkbilde åpnet"
            Remove-Item -Path $hidePidFile -Force -ErrorAction SilentlyContinue
        }
        'Restore' {
            Stop-Cover

            # Oppgavelinjen vises alltid igjen, også uten lagret tilstand (sikkerhetsnett ved pålogging).
            [GameHomeDesktop]::SetTaskbarVisible($true)
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
