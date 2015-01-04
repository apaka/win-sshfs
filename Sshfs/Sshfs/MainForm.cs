﻿#region

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Renci.SshNet;
using Sshfs.Properties;

#endregion

namespace Sshfs
{
    public partial class MainForm : Form
    {
        private readonly StringBuilder _balloonText = new StringBuilder(255);
        private readonly List<SftpDrive> _drives = new List<SftpDrive>();
        private readonly Regex _regex = new Regex(@"^New Drive\s\d{1,2}$", RegexOptions.Compiled);
        private readonly Queue<SftpDrive> _suspendedDrives = new Queue<SftpDrive>();
        private bool _balloonTipVisible;

        private int _lastindex = -1;
        private int _namecount;
        private bool _suspend;
        private bool _dirty;

        public MainForm()
        {
            InitializeComponent();
            Opacity = 0;
            driveListView.Columns[0].Width = driveListView.ClientRectangle.Width - 1;
            contextMenu.Renderer = new ContextMenuStripThemedRenderer();
          
        }


        protected override void OnLoad(EventArgs e)
        {

          
            notifyIcon.Text = Text = String.Format("Sshfs Manager {0}", Assembly.GetEntryAssembly().GetName().Version);
            portBox.Minimum = IPEndPoint.MinPort;
            portBox.Maximum = IPEndPoint.MaxPort;




            openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile,
                                                                            Environment.SpecialFolderOption.None);//Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile,Environment.SpecialFolderOption.DoNotVerify),".ssh");

           /* if (!Directory.Exists(openFileDialog.InitialDirectory))
                openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Personal,
                                                                            Environment.SpecialFolderOption.DoNotVerify);*/

            startupMenuItem.Checked = Utilities.IsAppRegistredForStarup();

            // _drives.Presist("config.xml",true);

            _drives.Load("config.xml");


            driveListView.BeginUpdate();
            for (int i = 0; i < _drives.Count; i++)
            {
                driveListView.Items.Add((_drives[i].Tag =
                                         new ListViewItem(_drives[i].Name, 0) {Tag = _drives[i]}) as ListViewItem);
                _drives[i].StatusChanged += drive_StatusChanged;
                if (_drives[i].Name.StartsWith("New Drive")) _namecount++;
            }


            if (driveListView.Items.Count != 0)
            {
                driveListView.SelectedIndices.Add(0);
            }


            driveListView.EndUpdate();

            //just to remove HScroll
            if (driveListView.Items.Count > 10)
            {
                driveListView.Items[10].EnsureVisible();
                driveListView.Items[0].EnsureVisible();
            }

            SetupPanels();


            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            base.OnLoad(e);
        }


        private void startupMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            if (startupMenuItem.Checked)
            {
                Utilities.RegisterForStartup();
            }
            else
            {
                Utilities.UnregisterForStarup();
            }
        }

        private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            Debug.WriteLine("PowerModeChange:{0}", e.Mode);
            _suspend = e.Mode == PowerModes.Suspend;

            if (e.Mode == PowerModes.Resume)
            {
                while (_suspendedDrives.Count > 0)
                {
                    MountDrive(_suspendedDrives.Dequeue());
                }
            }
        }

        private void SetupPanels()
        {
            buttonPanel.Enabled = removeButton.Enabled = fieldsPanel.Enabled = driveListView.Items.Count != 0;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                Visible = false;
                e.Cancel = true;
            }
            else
            {
                Debug.WriteLine("FormCOveride");
                if (_dirty)
                {
                    _drives.Presist("config.xml");
                }
                notifyIcon.Visible = false;
            }
            base.OnFormClosing(e);
        }

        private void addButton_Click(object sender, EventArgs e)
        {
            char letter;
            try
            {
                letter = Utilities.GetAvailableDrives().Except(_drives.Select(d => d.Letter)).First();
            }
            catch
            {
                MessageBox.Show("No more drive letters available", Text);
                return;
            }


            var drive = new SftpDrive
                            {
                                Name = String.Format("New Drive {0}", ++_namecount),
                                Port = 22,
                                Root = ".",
                                Letter = letter
                            };
            drive.StatusChanged += drive_StatusChanged;
            _drives.Add(drive);
            var item =
                (drive.Tag = new ListViewItem(drive.Name, 0) {Tag = drive, Selected = true}) as
                ListViewItem;

            driveListView.Items.Add(item
                );
            item.EnsureVisible();


            SetupPanels();
            _dirty = true;
        }

        private void drive_StatusChanged(object sender, EventArgs e)
        {
            var drive = sender as SftpDrive;

            Debug.WriteLine("Status Changed {0}:{1}", sender, drive.Status);

            if (_suspend && drive.Status == DriveStatus.Unmounted)
            {
                _suspendedDrives.Enqueue(drive);
            }

            if (!Visible)
            {
                ShowBallon(String.Format("{0} : {1}", drive.Name,
                                         drive.Status == DriveStatus.Mounted ? "Mounted" : "Unmounted"),false);
            }

            BeginInvoke(new MethodInvoker(() =>
                                              {
                                                  var item =
                                                      drive.Tag as ListViewItem;


                                                  if (item.Selected)
                                                  {
                                                      muButton.Text = drive.Status == DriveStatus.Mounted
                                                                          ? "Unmount"
                                                                          : "Mount";
                                                      muButton.Image = drive.Status == DriveStatus.Mounted
                                                                           ? Resources.unmount
                                                                           : Resources.mount;
                                                      muButton.Enabled = true;
                                                  }
                                                  item.ImageIndex = drive.Status == DriveStatus.Mounted ? 1 : 0;
                                              }));
        }

        private void removeButton_Click(object sender, EventArgs e)
        {
            if (driveListView.SelectedItems.Count != 0 &&
                MessageBox.Show("Do want to delete this drive ?", Text, MessageBoxButtons.YesNo) ==
                DialogResult.Yes)
            {
                var drive = driveListView.SelectedItems[0].Tag as SftpDrive;


                drive.StatusChanged -= drive_StatusChanged;
                drive.Unmount();
                _drives.Remove(drive);


                int next = driveListView.SelectedIndices[0] == driveListView.Items.Count - 1
                               ? driveListView.SelectedIndices[0] - 1
                               : driveListView.SelectedIndices[0];

                driveListView.Items.Remove(driveListView.SelectedItems[0]);

                if (next != -1)
                {
                    _lastindex = -1;
                    driveListView.SelectedIndices.Add(next);
                    driveListView.Items[next].EnsureVisible();
                }

                SetupPanels();
                _dirty = true;
            }
        }

        private void listView_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            if (e.IsSelected && _lastindex != e.ItemIndex)
            {
                _lastindex = e.ItemIndex;

                var drive = e.Item.Tag as SftpDrive;

                nameBox.Text = drive.Name;
                hostBox.Text = drive.Host;
                portBox.Value = drive.Port;
                userBox.Text = drive.Username;
                authCombo.SelectedIndex = drive.ConnectionType == ConnectionType.Password ? 0 : 1;
                storePasswordCheck.Checked = drive.StorePassword;
                letterBox.BeginUpdate();

                letterBox.Items.Clear();

                letterBox.Items.AddRange(
                    Utilities.GetAvailableDrives().Except(_drives.Select(d => d.Letter)).Select(
                        l => String.Format("{0} :", l)).ToArray());
                letterBox.Items.Add(String.Format("{0} :", drive.Letter));


                letterBox.SelectedIndex = letterBox.FindString(drive.Letter.ToString());

                letterBox.EndUpdate();

                passwordBox.Text = drive.Password;
                directoryBox.Text = drive.Root;
                mountCheck.Checked = drive.Automount;
                passwordBox.Text = drive.ConnectionType == ConnectionType.Password ? drive.Password : "";
                privateKeyBox.Text = drive.PrivateKey;
                passphraseBox.Text = drive.ConnectionType == ConnectionType.PrivateKey ? drive.Password : "";
                muButton.Text = drive.Status == DriveStatus.Mounted ? "Unmount" : "Mount";
                muButton.Image = drive.Status == DriveStatus.Mounted ? Resources.unmount : Resources.mount;
                muButton.Enabled = (drive.Status == DriveStatus.Unmounted || drive.Status == DriveStatus.Mounted);
            }
        }

        private void updateAuthVisibility()
        {
            switch (authCombo.SelectedIndex)
            {
                case 0:
                    passwordBox.Visible = storePasswordCheck.Checked;
                    passphraseBox.Visible = false;
                    privateKeyBox.Visible = privateKeyButton.Visible = false;
                    break;
                case 1:
                    passwordBox.Visible = false;
                    passphraseBox.Visible = storePasswordCheck.Checked;
                    privateKeyBox.Visible = privateKeyButton.Visible = true;
                    break;
            }
        }

        private void authBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            authLabel.Text = String.Format("{0}:", authCombo.Text);
            updateAuthVisibility();
        }

        private void storePasswordCheck_CheckedChanged(object sender, EventArgs e)
        {
            passwordBox.Text = passphraseBox.Text = "";
            updateAuthVisibility();
        }

        private void keyButton_Click(object sender, EventArgs e)
        {
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                privateKeyBox.Text = openFileDialog.FileName;
            }
        }

        private void saveButton_Click(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(nameBox.Text))
            {
                MessageBox.Show("Drive name connot be empty", Text);
                nameBox.Focus();
                return;
            }
            var drive = driveListView.SelectedItems[0].Tag as SftpDrive;

            if ((_regex.IsMatch(nameBox.Text) || nameBox.Text == String.Format("{0}@'{1}'", drive.Username, drive.Host)) &&
                !String.IsNullOrEmpty(userBox.Text) && !String.IsNullOrEmpty(hostBox.Text))
            {
                nameBox.Text = String.Format("{0}@'{1}'", userBox.Text, hostBox.Text);
            }


            driveListView.SelectedItems[0].Text = drive.Name = nameBox.Text;
            drive.Host = hostBox.Text;
            drive.Port = (int)portBox.Value;
            drive.Username = userBox.Text;
            drive.ConnectionType = authCombo.SelectedIndex == 0 ? ConnectionType.Password : ConnectionType.PrivateKey;
            drive.StorePassword = storePasswordCheck.Checked;
            drive.Letter = letterBox.Text[0];
            drive.Root = directoryBox.Text.Trim();
            drive.Automount = mountCheck.Checked;
            drive.Password = authCombo.SelectedIndex == 0 ? passwordBox.Text : passphraseBox.Text;
            drive.PrivateKey = privateKeyBox.Text;
            _dirty = true;
        }

        private void MountDrive(SftpDrive drive)
        {
            Task.Factory.StartNew(() =>
                                      {
                                          try
                                          {
                                              drive.Mount();
                                          }
                                          catch (Exception e)
                                          {
                                              BeginInvoke(new MethodInvoker(() =>
                                                                                {
                                                                                    if (
                                                                                        (drive.Tag as ListViewItem)
                                                                                            .Selected)
                                                                                    {
                                                                                        muButton.Enabled
                                                                                            = true;
                                                                                    }
                                                                                }));


                                              if (Visible)
                                              {
                                                  BeginInvoke(
                                                      new MethodInvoker(
                                                          () =>
                                                          MessageBox.Show(this,
                                                                          String.Format("{0} could not connect:\n{1}",
                                                                                        drive.Name, e.Message), Text)));
                                              }
                                              else
                                              {
                                                  ShowBallon(String.Format("{0} : {1}", drive.Name, e.Message),true);
                                              }
                                          }
                                      });
        }

        private string showPasswordPrompt(string title)
        {
            Form prompt = new Form();
            prompt.Width = 200;
            prompt.Height = 80;
            prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
            prompt.Text = title;
            prompt.StartPosition = FormStartPosition.CenterScreen;
            TextBox textBox = new TextBox() { Left=10, Top=10, Width=125, Height=20, PasswordChar='*' };
            Button confirmation = new Button() { Left=140, Top=10, Width=35, Height=20, Text="Ok" };
            confirmation.Click += (sender, e) => { prompt.Close(); };
            prompt.Controls.Add(textBox);
            prompt.Controls.Add(confirmation);
            prompt.AcceptButton = confirmation;
            prompt.ShowDialog();
            return textBox.Text;
        }

        private void muButton_Click(object sender, EventArgs e)
        {
            var drive = driveListView.SelectedItems[0].Tag as SftpDrive;

            if (drive.Status == DriveStatus.Unmounted)
            {
                if (!drive.StorePassword)
                {
                    drive.Password = showPasswordPrompt(drive.ConnectionType == ConnectionType.Password ? "Password" : "Passphrase");
                }
                MountDrive(drive);
                muButton.Enabled = false;
            }
            else
            {
                drive.Unmount();
            }
        }


        private void driveListView_MouseUpDown(object sender, MouseEventArgs e)
        {
            if (driveListView.HitTest(e.X, e.Y).Item == null && driveListView.Items.Count != 0)
            {
                driveListView.SelectedIndices.Add(_lastindex);
            }
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            Visible = false;
            Opacity = 1;
            Shown -= MainForm_Shown;

            foreach (var drive in _drives.Where(d => d.Automount))
            {
                MountDrive(drive);
            }
            if (_drives.Count != 0 && _drives[0].Automount)
                muButton.Enabled = false;
            ;
        }

        private void openFileDialog_FileOk(object sender, CancelEventArgs e)
        {
            //don't check larger files
            if (new FileInfo(openFileDialog.FileName).Length>4*4*1024||!PrivateKeyFile.IsValid(openFileDialog.FileName))
            {
                
                MessageBox.Show(this,
                                "File doesn't seem to be a valid private key file",Text);
                e.Cancel = true;
            }
        
        }

        private void notifyIcon_MouseClick(object sender, MouseEventArgs e)
        {
            _balloonTipVisible = false;
            if (e.Button == MouseButtons.Left)
            {
                ReShow();
            }
        }

        private void exitMenuItem_Click(object sender, EventArgs e)
        {
            if(_drives.Any(i=>i.Status==DriveStatus.Mounted)&&MessageBox.Show(this,"Remote drives that are still connected will be closed on application exit.\nDo you wish to continue?",Text,MessageBoxButtons.YesNo)==DialogResult.No)
            {
                return;
            }


            Application.Exit();
        }

        private void aboutMenuItem_Click(object sender, EventArgs e)
        {
            var about = Application.OpenForms.OfType<AboutForm>().FirstOrDefault();
            if (about == null)
            {
                new AboutForm().ShowDialog(this);
            }
            else
            {
                about.Focus();
            }
        }

        private void showMenuItem_Click(object sender, EventArgs e)
        {
            ReShow();
        }

        public void ReShow()
        {
            
            TopMost = true;
            Visible = true;
            TopMost = false;
           
        }


        private void notifyIcon_BalloonTipClosed(object sender, EventArgs e)
        {
            _balloonTipVisible = false;
        }

        private void notifyIcon_BalloonTipShown(object sender, EventArgs e)
        {
            _balloonTipVisible = true;
        }

        private void ShowBallon(string text,bool error)
        {
            if (!_balloonTipVisible || (_balloonText.Length + text.Length) > 255)
            {
                _balloonText.Clear();
            }

            _balloonText.AppendLine(text);


            notifyIcon.ShowBalloonTip(0, Text, _balloonText.ToString().TrimEnd(),error?ToolTipIcon.Warning: ToolTipIcon.Info);
        }

        private void driveListView_ClientSizeChanged(object sender, EventArgs e)
        {
            Debug.WriteLine("CLIENT SIZE" + driveListView.ClientRectangle + driveListView.Columns[0].Width);

            //  driveListView.Scrollable = false;
            // driveListView.Refresh();
            driveListView.Columns[0].Width = driveListView.ClientRectangle.Width - 1;
        }


       
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
            
          //  _drives.Presist("config.xml");
           ;

            Parallel.ForEach(_drives.Where(d => d.Status != DriveStatus.Unmounted), d =>
                                                                                        {
                                                                                            d.StatusChanged -=
                                                                                                drive_StatusChanged;
                                                                                            d.Unmount();
                                                                                        });
            base.OnFormClosed(e);
        }

        private  void box_Leave(object sender, EventArgs e)
        {
            var box = sender as TextBox;
            box.Text = box.Text.Trim();
        }

        private void contextMenu_Opening(object sender, CancelEventArgs e)
        {
            mountMenuItem.Enabled = _drives.Any(drive => drive.Status == DriveStatus.Unmounted);
            unmountMenuItem.Enabled = _drives.Any(drive => drive.Status == DriveStatus.Mounted);
        }

        private void unmountMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            foreach (var drive in _drives.Where(d => d.Status == DriveStatus.Mounted))
            {
                var umitem = unmountMenuItem.DropDownItems.Add(drive.Name);
                umitem.Tag = drive;
                umitem.Click += umitem_Click;
            }
        }

        private void umitem_Click(object sender, EventArgs e)
        {
            ((sender as ToolStripItem).Tag as SftpDrive).Unmount();
        }


        private void unmountMenuItem_DropDownClosed(object sender, EventArgs e)
        {
            unmountMenuItem.DropDownItems.Clear();
        }

        private void mountMenuItem_DropDownClosed(object sender, EventArgs e)
        {
            mountMenuItem.DropDownItems.Clear();
        }

        private void mountMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            foreach (var drive in _drives.Where(d => d.Status == DriveStatus.Unmounted))
            {
                var mitem = mountMenuItem.DropDownItems.Add(drive.Name);
                mitem.Tag = drive;
                mitem.Click += mitem_Click;
            }
        }

        private void mitem_Click(object sender, EventArgs e)
        {
            var drive = (sender as ToolStripItem).Tag as SftpDrive;
            if (driveListView.SelectedItems[0].Tag == drive)
                muButton.Enabled = false;
            MountDrive(drive);
        }

        private void buttonPanel_Paint(object sender, PaintEventArgs e)
        {

        }
    }
}