using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using NAudio.CoreAudioApi;
using NAudio.Wave;

class Program {
    [STAThread]
    static void Main() {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new KeeperContext());
    }
}

public class KeeperContext : ApplicationContext {
    private NotifyIcon trayIcon;
    private WaveOutEvent output;
    private SilentProvider silentProvider;
    private System.Threading.Timer monitorTimer;
    private string selectedDevice;
    private bool isActive;

    public KeeperContext() {

        //StartupManager.ForceEnable("BT-ticking-fix");

        trayIcon = new NotifyIcon() {
            Icon = new Icon("Assets/appicon.ico"),
            ContextMenuStrip = new ContextMenuStrip(),
            Visible = true,
            Text = "Bluetooth ticking fix",
        };

        trayIcon.ContextMenuStrip.Items.Add("Exit", null, Exit);

        trayIcon.MouseClick += (s, e) => ShowSettings(s, e);
    }

    private void ShowSettings(object sender, EventArgs e) {
        using (var settingsForm = new SettingsForm(selectedDevice, isActive)) {
            if (settingsForm.ShowDialog() == DialogResult.OK) {
                selectedDevice = settingsForm.SelectedDevice;
                bool newActiveState = settingsForm.IsActive;

                if (newActiveState && !isActive) {
                    StartMonitoring();
                } else if (!newActiveState && isActive) {
                    StopMonitoring();
                }

                isActive = newActiveState;
            }
        }
    }

    private void StartMonitoring() {
        isActive = true;
        trayIcon.Text = "BT ticking fix - Active";
        monitorTimer = new System.Threading.Timer(MonitorDevice, null, 0, 5000);
    }

    private void StopMonitoring() {
        isActive = false;
        trayIcon.Text = "BT ticking fix - Inactive";

        monitorTimer?.Dispose();
        monitorTimer = null;

        if (output != null) {
            output.Stop();
            output.Dispose();
            output = null;
            silentProvider = null;
        }
    }

    private void MonitorDevice(object state) {
        if (!isActive || string.IsNullOrEmpty(selectedDevice))
            return;

        bool connected = IsDeviceConnected(selectedDevice);

        if (connected) {
            if (output == null) {
                try {
                    silentProvider = new SilentProvider(new WaveFormat(8000, 16, 2));
                    output = new WaveOutEvent();
                    output.Init(silentProvider);
                    output.Play();
                } catch { }
            }
        } else {
            if (output != null) {
                output.Stop();
                output.Dispose();
                output = null;
                silentProvider = null;
            }
        }
    }

    private bool IsDeviceConnected(string deviceName) {
        try {
            var enumr = new MMDeviceEnumerator();
            var devices = enumr.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            return devices.Any(d =>
                d.FriendlyName.Contains(deviceName, StringComparison.OrdinalIgnoreCase)
            );
        } catch {
            return false;
        }
    }

    private void Exit(object sender, EventArgs e) {
        StopMonitoring();
        trayIcon.Visible = false;
        Application.Exit();
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            StopMonitoring();
            trayIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}

public class SettingsForm : Form {
    private ComboBox deviceComboBox;
    private CheckBox activeCheckBox;
    private Button okButton;
    private Button cancelButton;

    public string SelectedDevice { get; private set; }
    public bool IsActive { get; private set; }

    public SettingsForm(string currentDevice, bool currentActiveState) {
        InitializeComponent(currentDevice, currentActiveState);
        LoadDevicesAsync();
    }

    private void InitializeComponent(string currentDevice, bool currentActiveState) {
        Text = "Bluetooth ticking fix - Settings";
        Size = new Size(400, 200);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        Label deviceLabel = new Label() {
            Text = "Select Bluetooth Device:",
            Location = new Point(20, 20),
            Size = new Size(350, 20),
        };

        deviceComboBox = new ComboBox() {
            Location = new Point(20, 45),
            Size = new Size(340, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        deviceComboBox.SelectedIndexChanged += DeviceComboBox_SelectedIndexChanged;

        activeCheckBox = new CheckBox() {
            Text = "Enable Monitoring",
            Location = new Point(20, 85),
            Size = new Size(340, 25),
            Checked = currentActiveState,
            Enabled = !string.IsNullOrEmpty(currentDevice),
        };

        okButton = new Button() {
            Text = "OK",
            Location = new Point(200, 120),
            Size = new Size(80, 30),
            DialogResult = DialogResult.OK,
        };
        okButton.Click += OkButton_Click;

        cancelButton = new Button() {
            Text = "Cancel",
            Location = new Point(290, 120),
            Size = new Size(80, 30),
            DialogResult = DialogResult.Cancel,
        };

        Controls.AddRange(new Control[] {
            deviceLabel, deviceComboBox, activeCheckBox, okButton, cancelButton
        });

        AcceptButton = okButton;
        CancelButton = cancelButton;

        SelectedDevice = currentDevice;
        IsActive = currentActiveState;
    }

    private async void LoadDevicesAsync() {
        deviceComboBox.Items.Clear();
        deviceComboBox.Items.Add("Loading devices...");
        deviceComboBox.Enabled = false;

        var devices = await Task.Run(() => {
            var enumr = new MMDeviceEnumerator();
            var list = enumr.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All);

            return list
                .Where(d => d.State == DeviceState.Active)
                .Select(d => d.FriendlyName)
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        });

        deviceComboBox.Items.Clear();
        deviceComboBox.Items.AddRange(devices.ToArray());
        deviceComboBox.Enabled = true;

        if (!string.IsNullOrEmpty(SelectedDevice) &&
            deviceComboBox.Items.Contains(SelectedDevice)) {
            deviceComboBox.SelectedItem = SelectedDevice;
        }
    }

    private void DeviceComboBox_SelectedIndexChanged(object sender, EventArgs e) {
        activeCheckBox.Enabled = deviceComboBox.SelectedItem != null;
    }

    private void OkButton_Click(object sender, EventArgs e) {
        if (deviceComboBox.SelectedItem == null && activeCheckBox.Checked) {
            MessageBox.Show(
                "Please select a device before enabling monitoring.",
                "No Device Selected",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            DialogResult = DialogResult.None;
            return;
        }

        SelectedDevice = deviceComboBox.SelectedItem?.ToString();
        IsActive = activeCheckBox.Checked;
    }
}

public class SilentProvider : IWaveProvider {
    private readonly WaveFormat format;
    private readonly byte[] silenceBuffer;

    public SilentProvider(WaveFormat waveFormat) {
        format = waveFormat;
        silenceBuffer = new byte[waveFormat.AverageBytesPerSecond / 10];
    }

    public WaveFormat WaveFormat => format;

    public int Read(byte[] buffer, int offset, int count) {
        int bytesToCopy = Math.Min(count, silenceBuffer.Length);
        Array.Copy(silenceBuffer, 0, buffer, offset, bytesToCopy);
        return bytesToCopy;
    }
}
