using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Net;
using HtmlAgilityPack;
using System.Web;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using System.IO;
using Microsoft.Win32;
using System.Reflection;
using System.Net.Sockets;

namespace AVK_Scraper
{
    public partial class Form1 : Form
    {
        // Extracted cars by URL
        public static Dictionary<string, Car> carsByURL = new Dictionary<string, Car>();

        // New cars found just now! (use to update grid + show notification)
        static List<Car> newCars = new List<Car>();

        // Actual status
        public static string actualStatus = "idle";

        // Delay between reuqesut
        public static int delaySec = 2;

        // Path to alarm song
        string alarmSongPath = String.Empty;

        // Play song alarm?
        bool playSongAlarm = true;

        // Stop playing song after XX sec
        int alarmPlaySec = 4;

        // Scanning time - FROM - TO
        public static DateTime scanTime_from = DateTime.Now;
        public static DateTime scanTime_to = DateTime.Now;

        // Refresh grid?
        static public bool refreshGrid = false;

        // The path to the key where Windows looks for startup applications
        RegistryKey rkApp = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
        bool blockRegistryChange = true;

        // Next listing ID
        static int nextListingID = 0;

        // All extracted listings by ID
        static Dictionary<int, Listing> extractedListingsByID = new Dictionary<int, Listing>();

        // New correct listings found just now! (use to update grid + show notification)
        static List<Listing> newProperListings = new List<Listing>();

        public Form1()
        {
            // Text on warning winodw
            string title = "Unable to start application";
            string bodyMessage = "Application expired. Please contact owner of this application for new version.";

            // Get actual date
            DateTime actualDate = GetNetworkTime();

            // Date when program will expire => 2016/09/30
            DateTime expireDate = new DateTime(2119, 09, 30);

            // Program expire! -> show warning window
            if (actualDate > expireDate)
            {
                // Warning window
                MessageBox.Show(bodyMessage, title);

                // Close application
                Environment.Exit(0);
            }

            // Else start application
            else
            {
                // Initialize program window
                InitializeComponent();

                // Load config (previously variables used on textboxes / datetimepickers etc.)
                LoadConfig();

                // Check to see the current state (running at startup or not)
                string programName = Path.GetFileName(System.Reflection.Assembly.GetEntryAssembly().Location).ToLower().Replace(".exe", String.Empty).Trim();
                //MessageBox.Show(programName);
                if (rkApp.GetValue(programName) == null)
                {
                    checkBox6.Checked = false;
                }
                else
                {
                    // The value exists, the application is set to run at startup
                    checkBox6.Checked = true;
                }

                // Unlock registry change
                blockRegistryChange = false;

                // Tab as default
                SelectTab(2);

                // Start thread used for scanning site
                ToriScanner.Start();

                // Start thread used for sending emails
                EmailSender.Start();

                // Speed datagridview (http://stackoverflow.com/questions/118528/horrible-redraw-performance-of-the-datagridview-on-one-of-my-two-screens)
                typeof(DataGridView).InvokeMember("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty, null, dataGridView2, new object[] { true });

                // Thread for refreshing window (at the end)
                Thread mainThread = new Thread(new ThreadStart(MainThread));
                mainThread.IsBackground = true;
                mainThread.Start();
            }
        }

        public static DateTime GetNetworkTime()
        {
            // Actual try ...
            int actualTry = 0;

            // Max 5 tries
            while (++actualTry <= 5)
            {
                try
                {
                    //default Windows time server
                    const string ntpServer = "time1.google.com";

                    // NTP message size - 16 bytes of the digest (RFC 2030)
                    var ntpData = new byte[48];

                    //Setting the Leap Indicator, Version Number and Mode values
                    ntpData[0] = 0x1B; //LI = 0 (no warning), VN = 3 (IPv4 only), Mode = 3 (Client Mode)

                    var addresses = Dns.GetHostEntry(ntpServer).AddressList;

                    //The UDP port number assigned to NTP is 123
                    var ipEndPoint = new IPEndPoint(addresses[0], 123);
                    //NTP uses UDP
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

                    socket.Connect(ipEndPoint);

                    //Stops code hang if NTP is blocked
                    socket.ReceiveTimeout = 3000;

                    socket.Send(ntpData);
                    socket.Receive(ntpData);
                    socket.Close();

                    //Offset to get to the "Transmit Timestamp" field (time at which the reply 
                    //departed the server for the client, in 64-bit timestamp format."
                    const byte serverReplyTime = 40;

                    //Get the seconds part
                    ulong intPart = BitConverter.ToUInt32(ntpData, serverReplyTime);

                    //Get the seconds fraction
                    ulong fractPart = BitConverter.ToUInt32(ntpData, serverReplyTime + 4);

                    //Convert From big-endian to little-endian
                    intPart = SwapEndianness(intPart);
                    fractPart = SwapEndianness(fractPart);

                    var milliseconds = (intPart * 1000) + ((fractPart * 1000) / 0x100000000L);

                    //**UTC** time
                    var networkDateTime = (new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc)).AddMilliseconds((long)milliseconds);

                    return networkDateTime.ToLocalTime();
                }
                catch (Exception exceptionObj)
                {
                    // Wait 10sec. and try again
                    Thread.Sleep(10 * 1000);
                    continue;
                }
            }

            return new DateTime();
        }

        // stackoverflow.com/a/3294698/162671
        static uint SwapEndianness(ulong x)
        {
            return (uint)(((x & 0x000000ff) << 24) +
                           ((x & 0x0000ff00) << 8) +
                           ((x & 0x00ff0000) >> 8) +
                           ((x & 0xff000000) >> 24));
        }

        void LoadConfig()
        {
            string configFilePath = String.Format("{0}\\config.ini", System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location));

            if (!File.Exists(configFilePath))
                using (File.Create(configFilePath)) { }

            foreach (string line in File.ReadAllLines(configFilePath))
            {
                if (String.IsNullOrEmpty(line)) { continue; }

                // Load Scan Settings
                if (line.StartsWith("[SCAN-URL-KEYWORDS] ")) 
                {
                    // Fix line
                    string line_fixed = line.Replace("[SCAN-URL-KEYWORDS] ", String.Empty).Trim();

                    // Scan URL + keywords
                    string[] scanURL_keywords = Regex.Split(line_fixed, @"\|:\|");

                    // Send to function
                    AddNewScanSettings(scanURL_keywords[0], scanURL_keywords[1]);
                }

                // Other configs
                else
                {
                    Match control_S = Regex.Match(line, @"\[(.*?)\] (.*)");
                    if (control_S.Success)
                    {
                        string loadedValue = control_S.Groups[2].Value.Trim();

                        Control[] controls = this.Controls.Find(control_S.Groups[1].Value, true);
                        if (controls.Length == 1 && !String.IsNullOrEmpty(control_S.Groups[2].Value))
                        {
                            Control controlToLoad = controls[0];

                            // If text box
                            if (controlToLoad is TextBox)
                            {
                                ((TextBox)controlToLoad).Text = loadedValue;
                            }

                            // If date time picker
                            else if (controlToLoad is DateTimePicker)
                            {
                                long ticks_tmp = 0;
                                if (Int64.TryParse(loadedValue, out ticks_tmp))
                                {
                                    ((DateTimePicker)controlToLoad).Value = new DateTime(ticks_tmp);
                                }
                            }

                            // If checkbox
                            else if (controlToLoad is CheckBox)
                            {
                                bool checked_tmp = false;
                                if (Boolean.TryParse(loadedValue, out checked_tmp))
                                {
                                    ((CheckBox)controlToLoad).Checked = checked_tmp;
                                }
                            }
                        }
                    }
                }
            }
        }

        void SaveConfig()
        {
            string configFilePath = String.Format("{0}\\config.ini", System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location));

            if (!File.Exists(configFilePath))
                using (File.Create(configFilePath)) { }

            using (StreamWriter streamWriter = new StreamWriter(configFilePath))
            {
                List<Control> controlsToSave = new List<Control>() { this };
                while (controlsToSave.Count > 0)
                {
                    Control controlToSave = controlsToSave[0];
                    controlsToSave.RemoveAt(0);

                    // Save textboxes
                    if (controlToSave is TextBox)
                    {
                        TextBox tBox = controlToSave as TextBox;
                        if (!tBox.ReadOnly && tBox.PasswordChar == '\0' && !tBox.Multiline)
                            streamWriter.WriteLine(String.Format("[{0}] {1}", controlToSave.Name, controlToSave.Text));
                    }

                    // Save date time picker
                    else if (controlToSave is DateTimePicker)
                    {
                        DateTimePicker dtPicker = controlToSave as DateTimePicker;
                        streamWriter.WriteLine(String.Format("[{0}] {1}", dtPicker.Name, dtPicker.Value.Ticks));
                    }

                    // Save checkboxes
                    else if (controlToSave is CheckBox)
                    {
                        CheckBox checkBox = controlToSave as CheckBox;

                        // Ignore checkbox (used for change registry key - loaded in different method)
                        //if (checkBox == checkBox6) { continue; }

                        streamWriter.WriteLine(String.Format("[{0}] {1}", checkBox.Name, checkBox.Checked));
                    }

                    if (controlToSave.Controls != null && controlToSave.Controls.Count > 0)
                    {
                        foreach (Control childControl in controlToSave.Controls) { controlsToSave.Add(childControl); }
                    }
                }

                // Lock scan settings
                lock (ToriScanner.scanSettingsByID.Values)
                {
                    // Loop each one
                    foreach (ScanSettings scanSetting in ToriScanner.scanSettingsByID.Values)
                    {
                        // Save config
                        streamWriter.WriteLine(String.Format("[SCAN-URL-KEYWORDS] {0} |:| {1} ", scanSetting.scanURL, scanSetting.GetKeywordsString()));
                    }
                }
            }
        }

        private void MainThread()
        {
            while (true)
            {
                Thread.Sleep(250);

                try
                {
                    this.Invoke((MethodInvoker)(() => this.toolStripStatusLabel4.Text = actualStatus));
                    this.Invoke((MethodInvoker)(() => this.toolStripStatusLabel6.Text = extractedListingsByID.Count.ToString()));
                    this.Invoke((MethodInvoker)(() => this.toolStripStatusLabel3.Text = String.Format(@"Actual Time: {0:hh\:mm\:ss}", DateTime.Now.TimeOfDay)));

                    // New car found!
                    lock (newProperListings)
                    {
                        if (newProperListings.Count > 0)
                        {
                            Listing newProperListing = newProperListings[0];
                            newProperListings.RemoveAt(0);

                            AlertNewListing(newProperListing);
                            refreshGrid = true;
                        }
                    }

                    // Refresh grid!
                    if (refreshGrid)
                    {
                        RefreshDataGrid();
                        refreshGrid = false;
                    }
                }
                catch { }
            }
        }

        public void RefreshDataGrid()
        {
            try
            {
                int totalRows = 0;
                dataGridView2.Invoke((MethodInvoker)(() => totalRows = this.dataGridView2.Rows.Count));
                DataGridViewRow singleRowObj = null;

                // Lock dictionary with all orders
                lock (extractedListingsByID.Values)
                {
                    HashSet<int> alreadyUpdatedListingIDs = new HashSet<int>();

                    // FIRST UPDATE DIRECTLY FROM TABLE (to maintain order and dont refresh whole table) //
                    for (int rowID = totalRows - 1; rowID >= 0; rowID--)
                    {
                        dataGridView2.Invoke((MethodInvoker)(() => singleRowObj = this.dataGridView2.Rows[rowID]));

                        int listingID_row = Convert.ToInt32(singleRowObj.Cells["ID_2"].Value);
                        if (extractedListingsByID.ContainsKey(listingID_row))
                            InsertProductIntoRow(singleRowObj, extractedListingsByID[listingID_row]);

                        alreadyUpdatedListingIDs.Add(listingID_row);
                    }

                    // NOW INSERT REST (this will execute at first run + when new order will be added) //
                    //int actualRow_tmp = -1;
                    foreach (Listing listingObj in extractedListingsByID.Values)
                    {
                        // Listing hidden ... skip!
                        if (listingObj.hide) { continue; }

                        // Already updated ... skip!
                        if (alreadyUpdatedListingIDs.Contains(listingObj.ID)) { continue; }

                        dataGridView2.Invoke((MethodInvoker)(() => this.dataGridView2.Rows.Insert(0, 1)));
                        InsertProductIntoRow(dataGridView2.Rows[0], listingObj);

                        dataGridView2.Invoke((MethodInvoker)(() => this.dataGridView2.ClearSelection()));
                        dataGridView2.Invoke((MethodInvoker)(() => this.dataGridView2.Rows[0].Selected = true));
                    }
                }
            }
            catch { }
        }

        void InsertProductIntoRow(DataGridViewRow row, Listing listing)
        {
            // Hidden?
            if (listing.hide)
            {
                // Remove from gridview!
                dataGridView2.Invoke((MethodInvoker)(() => dataGridView2.Rows.Remove(row)));
            }

            // Else add / update ...
            else
            {
                // Insert informations
                dataGridView2.Invoke((MethodInvoker)(() => row.Cells["ID_2"].Value = listing.ID));
                dataGridView2.Invoke((MethodInvoker)(() => row.Cells["Scan_URL_2"].Value = listing.fromScanURL));
                dataGridView2.Invoke((MethodInvoker)(() => row.Cells["Scan_Keywords_2"].Value = listing.fromKeywords));
                dataGridView2.Invoke((MethodInvoker)(() => row.Cells["Listing_Title"].Value = listing.title));
                dataGridView2.Invoke((MethodInvoker)(() => row.Cells["Listing_URL"].Value = listing.URL));
                dataGridView2.Invoke((MethodInvoker)(() => row.Cells["Source_URL"].Value = listing.fromScanURL));
                dataGridView2.Invoke((MethodInvoker)(() => row.Cells["added"].Value = listing.added));
                dataGridView2.Invoke((MethodInvoker)(() => row.Cells["email_status"].Value = listing.emailStatus));
            }
        }

        public static void AddNewListing(Listing newListing, bool firstScan)
        {
            // Set ID
            newListing.ID = ++nextListingID;

            // Add into main DB || Lock <- list was used by refresh thread
            lock (extractedListingsByID)
            {
                // From few scan URL might came same listing URL - ignore rest ones in this situation
                extractedListingsByID.Add(newListing.ID, newListing);
            }

            // Refresh grid
            Form1.refreshGrid = true;

            // Play alarm (if this is not first scan + some keywords was found)
            if (!firstScan && newListing.keywordsFound.Count > 0) 
            {
                // Lock <- list was used by refresh thread
                lock (newProperListings)
                {
                    newProperListings.Add(newListing); 
                }
            }
        }

        public void AlertNewListing(Listing newListing)
        {
            // Play alarm
            if (playSongAlarm) { PlayAlarm(); }

            // Add into email queue
            EmailSender.AddNewListing(newListing);

            // Tooltip if minimized
            //if (notifyIcon1.Visible)
            //{
            //    this.Invoke((MethodInvoker)(() => this.notifyIcon1.BalloonTipText = "New car found ..."));
            //    this.Invoke((MethodInvoker)(() => this.notifyIcon1.BalloonTipTitle = "Alert!"));
            //    this.Invoke((MethodInvoker)(() => this.notifyIcon1.ShowBalloonTip(1000)));
            //}

            // Show notification!
            /*
            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
            Notification fm = new Notification(this);
            //fm.ClientSize = new Size(200, 200);
            int left = workingArea.Width - fm.Width;
            int top = workingArea.Height - fm.Height;
            fm.Location = new Point(left, top);
            fm.ShowInTaskbar = false;
            fm.ShowIcon = false;
            fm.MinimizeBox = false;
            fm.MaximizeBox = false;
            fm.FormBorderStyle = FormBorderStyle.FixedToolWindow;
            //fm.Text = "New Car Found!";
            fm.TopMost = true;

            fm.Text = string.Empty;
            fm.ControlBox = false;
            //fm.FormBorderStyle = FormBorderStyle.SizableToolWindow;
            //fm.Show();
            this.Invoke((MethodInvoker)(() => fm.Show()));
            //MessageBox.Show(String.Format("New car URL:{1}{0}", carUrl, Environment.NewLine), "New Car!");
             */
        }

        private void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {
            Int32.TryParse(textBox2.Text, out delaySec);
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Save values from textboxes etc.
            SaveConfig();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFile = new OpenFileDialog();
            openFile.RestoreDirectory = true;
            openFile.Filter = "WAV Files (*.wav)|*.wav";
            DialogResult filePathWindow = openFile.ShowDialog();
            if (filePathWindow != DialogResult.OK) { return; }

            // Set textbox with loaded WAV Path
            textBox3.Text = openFile.FileName;
        }

        private void textBox3_TextChanged(object sender, EventArgs e)
        {
            alarmSongPath = textBox3.Text;
        }

        private void textBox5_TextChanged(object sender, EventArgs e)
        {
            Int32.TryParse(textBox5.Text, out alarmPlaySec);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            PlayAlarm();
        }

        void PlayAlarm()
        {
            if (!String.IsNullOrEmpty(alarmSongPath) && File.Exists(alarmSongPath))
            {
                System.Media.SoundPlayer player = new System.Media.SoundPlayer(alarmSongPath);
                player.Play();

                // Stop song after XX sec
                ThreadPool.QueueUserWorkItem((objectParameter) =>
                {
                    // Wait XX sec on different thread
                    Thread.Sleep(alarmPlaySec * 1000);

                    System.Media.SoundPlayer player_async = objectParameter as System.Media.SoundPlayer;
                    player_async.Stop();

                }, player);
            }
        }

        private void dateTimePicker1_ValueChanged(object sender, EventArgs e)
        {
            scanTime_from = dateTimePicker1.Value;
        }

        private void dateTimePicker2_ValueChanged(object sender, EventArgs e)
        {
            scanTime_to = dateTimePicker2.Value;
        }

        private void button3_Click(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(textBox6.Text);
            }
            catch { }
        }

        private void checkBox3_CheckedChanged(object sender, EventArgs e)
        {
            playSongAlarm = textBox3.Enabled = button1.Enabled = button2.Enabled = checkBox3.Checked;
        }

        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            //notifyIcon1.Visible = false;

            //this.WindowState = FormWindowState.Normal;
            //this.ShowInTaskbar = true;
            //notifyIcon1.Visible = false;
        }

        private void notifyIcon1_BalloonTipClicked(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            //notifyIcon1.Visible = false;
        }

        private void dataGridView2_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            var senderGrid = (DataGridView)sender;

            if ((senderGrid.Columns[e.ColumnIndex] is DataGridViewButtonColumn) && e.RowIndex >= 0)
            {
                DataGridViewColumn selectedColumn = senderGrid.Columns[e.ColumnIndex];
                switch (selectedColumn.Name)
                {
                    case "open":
                        string carURL = senderGrid.Rows[e.RowIndex].Cells["Listing_URL"].Value as string;
                        if (!String.IsNullOrEmpty(carURL)) { System.Diagnostics.Process.Start(carURL); }

                        break;
                }
            }
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            EmailSender.serverName = textBox1.Text;
        }

        private void textBox7_TextChanged(object sender, EventArgs e)
        {
            EmailSender.userName = textBox7.Text;
        }

        private void textBox8_TextChanged(object sender, EventArgs e)
        {
            EmailSender.password = textBox8.Text;
        }

        private void textBox9_TextChanged(object sender, EventArgs e)
        {
            EmailSender.fromAddress = textBox9.Text;
        }

        private void textBox10_TextChanged(object sender, EventArgs e)
        {
            // List with new emails
            List<string> emails = new List<string>();

            // Check if not empty + Split by comma
            if (!String.IsNullOrEmpty(textBox10.Text))
            {
                foreach (string oneEmail_ in textBox10.Text.Split(','))
                {
                    // Trim email
                    string oneEmail = oneEmail_.Trim();

                    // Add into tmp list
                    if (!emails.Contains(oneEmail)) { emails.Add(oneEmail); }
                }
            }

            // Replace list of emails
            EmailSender.toAddresses = emails;
        }

        private void button4_Click(object sender, EventArgs e)
        {
            EmailSender.SendTestEmail();
        }

        private void textBox4_TextChanged(object sender, EventArgs e)
        {
            Int32.TryParse(textBox4.Text, out EmailSender.portNumber);
        }

        private void checkBox4_CheckedChanged_1(object sender, EventArgs e)
        {
            if (!checkBox4.Checked && !checkBox5.Checked) { EmailSender.communicationType = HigLabo.Net.Smtp.SmtpEncryptedCommunication.None; }
            else if (checkBox4.Checked)
            {
                EmailSender.communicationType = HigLabo.Net.Smtp.SmtpEncryptedCommunication.Ssl;
                checkBox5.Checked = false;
            }
        }

        private void checkBox5_CheckedChanged(object sender, EventArgs e)
        {
            if (!checkBox4.Checked && !checkBox5.Checked) { EmailSender.communicationType = HigLabo.Net.Smtp.SmtpEncryptedCommunication.None; }
            else if (checkBox5.Checked)
            {
                EmailSender.communicationType = HigLabo.Net.Smtp.SmtpEncryptedCommunication.Tls;
                checkBox4.Checked = false;
            }
        }

        private void checkBox6_CheckedChanged(object sender, EventArgs e)
        {
            // Program name ...
            string programName = Path.GetFileName(System.Reflection.Assembly.GetEntryAssembly().Location).ToLower().Replace(".exe", String.Empty).Trim();
            if (!blockRegistryChange)
            {
                if (checkBox6.Checked)
                {
                    // Add the value in the registry so that the application runs at startup
                    rkApp.SetValue(programName, Application.ExecutablePath);
                }
                else
                {
                    // Remove the value from the registry so that the application doesn't start
                    rkApp.DeleteValue(programName, false);
                }
            }
        }

        private void button5_Click(object sender, EventArgs e)
        {
            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
            Notification fm = new Notification(this);
            //fm.ClientSize = new Size(200, 200);
            int left = workingArea.Width - fm.Width;
            int top = workingArea.Height - fm.Height;
            fm.Location = new Point(left, top);
            fm.ShowInTaskbar = false;
            fm.ShowIcon = false;
            fm.MinimizeBox = false;
            fm.MaximizeBox = false;
            fm.FormBorderStyle = FormBorderStyle.FixedToolWindow;
            //fm.Text = "New Car Found!";
            fm.TopMost = true;

            fm.Text = string.Empty;
            fm.ControlBox = false;
            //fm.FormBorderStyle = FormBorderStyle.SizableToolWindow;
            fm.Show();
        }

        public void SelectTab(int tabNumber)
        {
            tabControl1.SelectedIndex = tabNumber;
        }

        private void toolStripButton2_Click(object sender, EventArgs e)
        {
            // Mark all listings as hidden
            lock (extractedListingsByID.Values)
            {
                foreach (Listing listing in extractedListingsByID.Values) { listing.hide = true; }
            }

            // Force update
            refreshGrid = true;
        }

        private void button6_Click(object sender, EventArgs e)
        {
            // Send to function
            AddNewScanSettings(textBox11.Text, textBox12.Text);
        }

        private void AddNewScanSettings(string scanURL_, string keywords_string_)
        {
            // Create object
            ScanSettings scanSettings = new ScanSettings();

            // Scan URL
            scanSettings.scanURL = HttpUtility.HtmlDecode(scanURL_).Trim();

            // Not empty?
            if (!String.IsNullOrEmpty(scanSettings.scanURL))
            {
                // List with keywords
                scanSettings.keywords = new List<string>();

                // Add each one (if not empty)
                if (!String.IsNullOrEmpty(keywords_string_))
                {
                    foreach (string singleKeyword in keywords_string_.Split(','))
                    {
                        // Format and trim
                        string singleKeyword_fixed = HttpUtility.HtmlDecode(singleKeyword).Trim();

                        // If not existed alreday then add
                        if (!scanSettings.keywords.Contains(singleKeyword_fixed)) { scanSettings.keywords.Add(singleKeyword_fixed); }
                    }
                }

                // Add whole setting into scanner function!
                lock (ToriScanner.scanSettingsByID)
                {
                    // Not exist?
                    if (!ToriScanner.scanSettingsByID.ContainsKey(scanSettings.GetID())) 
                    {
                        // Add into dic
                        ToriScanner.scanSettingsByID.Add(scanSettings.GetID(), scanSettings); 

                        // Add into datagrid!
                        dataGridView1.Rows.Add(new object[] { scanSettings.scanURL, scanSettings.GetKeywordsString(), scanSettings.GetID() });
                    }
                }
            }
        }

        private void dataGridView1_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                int currentMouseOverRow = dataGridView1.HitTest(e.X, e.Y).RowIndex;

                if (currentMouseOverRow >= 0)
                {
                    DataGridViewCell hitCell = dataGridView1[dataGridView1.HitTest(e.X, e.Y).ColumnIndex, dataGridView1.HitTest(e.X, e.Y).RowIndex];
                    if (hitCell != null && hitCell is DataGridViewTextBoxCell)
                    {
                        // Remove previous selected rows
                        dataGridView1.ClearSelection();

                        dataGridView1.Rows[currentMouseOverRow].Selected = true;
                        ContextMenu contextMenu = new ContextMenu();

                        MenuItem menuItem = new MenuItem("Remove this URL and keywords");
                        menuItem.Click += menu_Click;

                        contextMenu.MenuItems.Add(menuItem);

                        contextMenu.Show(dataGridView1, new Point(e.X, e.Y));
                    }
                }
            }
        }

        void menu_Click(object sender, EventArgs e)
        {
            // Get row ID to delete
            Int32 rowToDelete = dataGridView1.Rows.GetFirstRow(DataGridViewElementStates.Selected);

            // Get scan setting ID from row that will be deleted
            string scanSettingID_toDelete = dataGridView1.Rows[rowToDelete].Cells["ID_1"].Value.ToString();

            // Remove from scanning
            lock (ToriScanner.scanSettingsByID)
            {
                if (ToriScanner.scanSettingsByID.ContainsKey(scanSettingID_toDelete)) { ToriScanner.scanSettingsByID.Remove(scanSettingID_toDelete); }
            }

            // Remove from datagrid and clear selection
            dataGridView1.Rows.RemoveAt(rowToDelete);
            dataGridView1.ClearSelection();
        }
    }
}
