﻿using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;
using EnrollmentStation.Code;

namespace EnrollmentStation
{
    public partial class MainForm : Form
    {
        private YubikeyNeoManager _neoManager;

        private DataStore _dataStore;
        private Settings _settings;

        public const string FileStore = "store.json";
        public const string FileSettings = "settings.json";

        private bool _devicePresent;
        private bool _hsmPresent;

        public MainForm()
        {
            CheckForIllegalCrossThreadCalls = false; //TODO: remove

            InitializeComponent();

            _neoManager = new YubikeyNeoManager();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            RefreshUserStore();
            RefreshSettings();

            RefreshSelectedKeyInfo();
            RefreshInsertedKey(_neoManager);

            RefreshHsm();

            // Start worker that checks for inserted yubikeys
            YubikeyDetector.Instance.StateChanged += YubikeyStateChange;
            YubikeyDetector.Instance.Start();

            if (!File.Exists(FileSettings))
            {
                DlgSettings dialog = new DlgSettings(_settings);
                var res = dialog.ShowDialog();

                if (res != DialogResult.OK)
                {
                    MessageBox.Show("You have to set the settings. Restart the application to set the settings.");
                    Close();
                }
            }
        }

        private void YubikeyStateChange()
        {
            using (YubikeyDetector.ExclusiveLock exclusiveNeo = YubikeyDetector.Instance.GetExclusiveLock())
            using (YubikeyNeoManager neoManager = new YubikeyNeoManager())
            {
                _devicePresent = neoManager.RefreshDevice();

                bool enableCCID;

                YubicoNeoMode currentMode = neoManager.GetMode();

                switch (currentMode)
                {
                    case YubicoNeoMode.OtpOnly:
                    case YubicoNeoMode.U2fOnly:
                    case YubicoNeoMode.OtpU2f:
                    case YubicoNeoMode.OtpOnly_WithEject:
                    case YubicoNeoMode.U2fOnly_WithEject:
                    case YubicoNeoMode.OtpU2f_WithEject:
                        enableCCID = true;
                        break;
                    case YubicoNeoMode.CcidOnly:
                    case YubicoNeoMode.OtpCcid:
                    case YubicoNeoMode.U2fCcid:
                    case YubicoNeoMode.OtpU2fCcid:
                    case YubicoNeoMode.CcidOnly_WithEject:
                    case YubicoNeoMode.OtpCcid_WithEject:
                    case YubicoNeoMode.U2fCcid_WithEject:
                    case YubicoNeoMode.OtpU2fCcid_WithEject:
                        enableCCID = false;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                btnEnableCCID.Text = enableCCID ? "Enable CCID" : "Disable CCID";

                btnEnableCCID.Enabled = _devicePresent;
                btnExportCert.Enabled = _devicePresent & !enableCCID;
                btnViewCert.Enabled = _devicePresent & !enableCCID;

                RefreshHsm();
                RefreshInsertedKey(neoManager);
            }
        }

        private void RefreshSelectedKeyInfo()
        {
            foreach (Control control in gbSelectedKey.Controls)
            {
                control.Visible = lstItems.SelectedItems.Count == 1;
            }

            foreach (Control control in gbSelectedKeyCertificate.Controls)
            {
                control.Visible = lstItems.SelectedItems.Count == 1;
            }

            if (lstItems.SelectedItems.Count <= 0)
                return;

            EnrolledYubikey item = lstItems.SelectedItems[0].Tag as EnrolledYubikey;

            if (item == null)
                return;

            lblYubikeySerial.Text = item.DeviceSerial.ToString();
            lblYubikeyFirmware.Text = item.YubikeyVersions.NeoFirmware;
            lblYubikeyPivVersion.Text = item.YubikeyVersions.PivApplet;

            lblCertCA.Text = item.CA;
            lblCertEnrolledOn.Text = item.EnrolledAt.ToString();
            lblCertSerial.Text = item.Certificate.Serial;
            lblCertThumbprint.Text = item.Certificate.Thumbprint;
            lblCertUser.Text = item.Username;
        }

        private void RefreshHsm()
        {
            _hsmPresent = HsmRng.IsHsmPresent();

            lblHSMPresent.Text = "HSM present: " + (_hsmPresent ? "Yes" : "No");
        }

        private void RefreshInsertedKey(YubikeyNeoManager neo)
        {
            foreach (Control control in gbInsertedKey.Controls)
            {
                control.Visible = _devicePresent;
            }

            if (!_devicePresent)
                return;

            lblInsertedSerial.Text = neo.GetSerialNumber().ToString();
            lblInsertedFirmware.Text = neo.GetVersion().ToString();
            lblInsertedMode.Text = neo.GetMode().ToString();

            using (YubikeyPivTool piv = new YubikeyPivTool())
            {
                lblInsertedHasBeenEnrolled.Text = (!piv.Authenticate(YubikeyPivTool.DefaultManagementKey)).ToString();
                lblInsertedPinTries.Text = piv.GetPinTriesLeft().ToString();
            }
        }

        private void RefreshUserStore()
        {
            _dataStore = DataStore.Load(FileStore);

            lstItems.Items.Clear();

            foreach (EnrolledYubikey yubikey in _dataStore.Yubikeys)
            {
                ListViewItem lsItem = new ListViewItem();

                lsItem.Tag = yubikey;

                // Fields
                // Serial
                lsItem.Text = yubikey.DeviceSerial.ToString();

                // User
                lsItem.SubItems.Add(yubikey.Username);

                // Enrolled
                lsItem.SubItems.Add(yubikey.EnrolledAt.ToLocalTime().ToString());

                // Certificate Serial
                lsItem.SubItems.Add(yubikey.Certificate != null ? yubikey.Certificate.Serial : "<unknown>");

                lstItems.Items.Add(lsItem);
            }
        }

        private void RefreshSettings()
        {
            _settings = Settings.Load(FileSettings);
        }

        private void btnViewCert_Click(object sender, EventArgs e)
        {
            if (!_devicePresent)
                return;

            X509Certificate2 cert;

            using (YubikeyPivTool pivTool = YubikeyPivTool.StartPiv())
                cert = pivTool.GetCertificate9a();

            if (cert == null)
                MessageBox.Show("No certificate on device.", "No Certificate", MessageBoxButtons.OK);
            else
                X509Certificate2UI.DisplayCertificate(cert);
        }

        private void btnExportCert_Click(object sender, EventArgs e)
        {
            if (!_devicePresent)
                return;

            X509Certificate2 cert;
            using (YubikeyPivTool pivTool = YubikeyPivTool.StartPiv())
                cert = pivTool.GetCertificate9a();

            if (cert == null)
            {
                MessageBox.Show("No certificate on device.", "No Certificate", MessageBoxButtons.OK);
                return;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.FileName = _neoManager.GetSerialNumber() + "-" + cert.SerialNumber + ".crt"; //TODO: GetSerialNumber() can possibly fail

            DialogResult dlgResult = saveFileDialog.ShowDialog();

            if (dlgResult != DialogResult.OK)
                return;

            using (Stream fs = saveFileDialog.OpenFile())
            {
                byte[] data = cert.GetRawCertData();
                fs.Write(data, 0, data.Length);
            }
        }

        private void btnEnableCCID_Click(object sender, EventArgs e)
        {
            YubicoNeoMode currentMode = 0; // _neoManager.GetMode();
            YubicoNeoMode newMode;

            switch (currentMode)
            {
                case YubicoNeoMode.OtpOnly:
                    newMode = YubicoNeoMode.OtpCcid;
                    break;
                case YubicoNeoMode.U2fOnly:
                    newMode = YubicoNeoMode.U2fCcid;
                    break;
                case YubicoNeoMode.OtpU2f:
                    newMode = YubicoNeoMode.OtpU2fCcid;
                    break;
                case YubicoNeoMode.OtpOnly_WithEject:
                    newMode = YubicoNeoMode.OtpCcid_WithEject;
                    break;
                case YubicoNeoMode.U2fOnly_WithEject:
                    newMode = YubicoNeoMode.U2fCcid_WithEject;
                    break;
                case YubicoNeoMode.OtpU2f_WithEject:
                    newMode = YubicoNeoMode.OtpU2fCcid_WithEject;
                    break;
                case YubicoNeoMode.CcidOnly:
                    newMode = YubicoNeoMode.OtpOnly;
                    break;
                case YubicoNeoMode.OtpCcid:
                    newMode = YubicoNeoMode.OtpOnly;
                    break;
                case YubicoNeoMode.U2fCcid:
                    newMode = YubicoNeoMode.U2fOnly;
                    break;
                case YubicoNeoMode.OtpU2fCcid:
                    newMode = YubicoNeoMode.OtpU2f;
                    break;
                case YubicoNeoMode.CcidOnly_WithEject:
                    newMode = YubicoNeoMode.OtpOnly_WithEject;
                    break;
                case YubicoNeoMode.OtpCcid_WithEject:
                    newMode = YubicoNeoMode.OtpOnly_WithEject;
                    break;
                case YubicoNeoMode.U2fCcid_WithEject:
                    newMode = YubicoNeoMode.U2fOnly_WithEject;
                    break;
                case YubicoNeoMode.OtpU2fCcid_WithEject:
                    newMode = YubicoNeoMode.OtpU2f_WithEject;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (newMode != currentMode)
                _neoManager.SetMode(newMode);
        }

        private void btnEnrollKey_Click(object sender, EventArgs e)
        {
            DlgEnroll enroll = new DlgEnroll(_settings, _dataStore);
            enroll.ShowDialog();

            RefreshUserStore();
        }

        private void exportCertificateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (lstItems.SelectedItems.Count <= 0)
                return;

            EnrolledYubikey item = lstItems.SelectedItems[0].Tag as EnrolledYubikey;
            if (item == null)
                return;

            X509Certificate2 cert = new X509Certificate2(item.Certificate.RawCertificate);

            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.FileName = item.DeviceSerial + "-" + cert.SerialNumber + ".crt";

            DialogResult dlgResult = saveFileDialog.ShowDialog();

            if (dlgResult != DialogResult.OK)
                return;

            using (Stream fs = saveFileDialog.OpenFile())
            {
                byte[] data = cert.GetRawCertData();
                fs.Write(data, 0, data.Length);
            }
        }

        private void viewCertificateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (lstItems.SelectedItems.Count <= 0)
                return;

            EnrolledYubikey item = lstItems.SelectedItems[0].Tag as EnrolledYubikey;
            if (item == null)
                return;

            X509Certificate2 cert = new X509Certificate2(item.Certificate.RawCertificate);
            X509Certificate2UI.DisplayCertificate(cert);
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _neoManager.Dispose();
        }

        private void tsbSettings_Click(object sender, EventArgs e)
        {
            DlgSettings dialog = new DlgSettings(_settings);
            dialog.ShowDialog();
        }

        private void lstItems_SelectedIndexChanged(object sender, EventArgs e)
        {
            RefreshSelectedKeyInfo();
        }

        private void resetPINToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (lstItems.SelectedItems.Count <= 0)
                return;

            EnrolledYubikey item = lstItems.SelectedItems[0].Tag as EnrolledYubikey;
            if (item == null)
                return;

            DlgResetPin changePin = new DlgResetPin(item);
            changePin.ShowDialog();
        }

        private void resetToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (lstItems.SelectedItems.Count <= 0)
                return;

            EnrolledYubikey item = lstItems.SelectedItems[0].Tag as EnrolledYubikey;
            if (item == null)
                return;

            DialogResult dlgResult = MessageBox.Show("This will reset the Yubikey, wiping the PIN, PUK, management key and certificates. This will NOT revoke the certifcates. Proceeed?", "Reset YubiKey", MessageBoxButtons.YesNo, MessageBoxIcon.Stop, MessageBoxDefaultButton.Button2);

            if (dlgResult != DialogResult.Yes)
                return;

            DlgPleaseInsertYubikey yubiWaiter = new DlgPleaseInsertYubikey(item);
            DialogResult result = yubiWaiter.ShowDialog();

            if (result != DialogResult.OK)
                return;

            using (YubikeyDetector.ExclusiveLock exclusive = YubikeyDetector.Instance.GetExclusiveLock())
            using (YubikeyPivTool pivTool = YubikeyPivTool.StartPiv())
            {
                // Attempt an invalid PIN X times
                pivTool.BlockPin();

                // Attempt an invalid PUK X times
                pivTool.BlockPuk();

                bool resetDevice = pivTool.ResetDevice();
                if (!resetDevice)
                {
                    MessageBox.Show("Unable to reset the yubikey. Try resetting it manually.", "An error occurred.", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void revokeToolStripMenuItem_Click_1(object sender, EventArgs e)
        {
            if (lstItems.SelectedItems.Count <= 0)
                return;

            EnrolledYubikey item = lstItems.SelectedItems[0].Tag as EnrolledYubikey;
            if (item == null)
                return;

            DialogResult dlgResult = MessageBox.Show("Revoke the certificate enrolled at " + item.EnrolledAt.ToLocalTime() + " for " + item.Username + ". This action will revoke " +
                   "the certificate, but will NOT wipe the yubikey." + Environment.NewLine + Environment.NewLine +
                   "Certificate: " + item.Certificate.Serial + Environment.NewLine +
                   "Subject: " + item.Certificate.Subject + Environment.NewLine +
                   "Issuer: " + item.Certificate.Issuer + Environment.NewLine +
                   "Valid: " + item.Certificate.StartDate + " to " + item.Certificate.ExpireDate,
                   "Revoke certificate?", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);

            if (dlgResult != DialogResult.Yes)
                return;

            try
            {
                CertificateUtilities.RevokeCertificate(item.CA, item.Certificate.Serial);
            }
            catch (Exception ex)
            {
                MessageBox.Show("We were unable to revoke the certificate. Details: " + ex.Message, "An error occurred.", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Remove the item from the datastore
            _dataStore.Remove(item);
        }

        private void terminateToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            if (lstItems.SelectedItems.Count <= 0)
                return;

            EnrolledYubikey item = lstItems.SelectedItems[0].Tag as EnrolledYubikey;
            if (item == null)
                return;

            X509Certificate2 currentCert = new X509Certificate2(item.Certificate.RawCertificate);

            DialogResult dlgResult = MessageBox.Show("This will terminate the Yubikey, wiping the PIN, PUK, Management Key and Certificates. " +
                                                     "This will also revoke the certificiate. Proceeed?" + Environment.NewLine + Environment.NewLine +
                                                     "Will revoke: " + currentCert.Subject + Environment.NewLine +
                                                     "By: " + currentCert.Issuer, "Terminate (WILL revoke)",
                                                     MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button2);

            if (dlgResult != DialogResult.Yes)
                return;

            DlgPleaseInsertYubikey yubiWaiter = new DlgPleaseInsertYubikey(item);
            DialogResult result = yubiWaiter.ShowDialog();

            if (result != DialogResult.OK)
                return;

            // Begin
            try
            {
                CertificateUtilities.RevokeCertificate(item.CA, item.Certificate.Serial);

                // Revoked
                _dataStore.Remove(item);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to revoke certificate " + item.Certificate.Serial + " of " + item.CA +
                    " enrolled on " + item.EnrolledAt + ". There was an error." + Environment.NewLine +
                    Environment.NewLine + ex.Message, "An error occurred.", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                return;
            }

            // Wipe the Yubikey
            using (YubikeyDetector.ExclusiveLock exclusive = YubikeyDetector.Instance.GetExclusiveLock())
            using (YubikeyPivTool piv = new YubikeyPivTool())
            {
                piv.BlockPin();
                piv.BlockPuk();

                bool reset = piv.ResetDevice();
                if (!reset)
                {
                    MessageBox.Show("Unable to reset the yubikey. Try resetting it manually.", "An error occurred.", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            _dataStore.Save(MainForm.FileStore);

        }
    }
}