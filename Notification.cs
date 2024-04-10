using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AVK_Scraper
{
    public partial class Notification : Form
    {
        // Main window
        Form1 mainWindow = null;

        public Notification(Form1 mainWindow_)
        {
            // Get main window
            this.mainWindow = mainWindow_;

            InitializeComponent();
        }

        private void Notification_Shown(object sender, EventArgs e)
        {
            //Determine "rightmost" screen
            Screen rightmost = Screen.AllScreens[0];
            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.WorkingArea.Right > rightmost.WorkingArea.Right)
                    rightmost = screen;
            }

            this.Left = rightmost.WorkingArea.Right - this.Width - 6;
            this.Top = rightmost.WorkingArea.Bottom - this.Height - 6;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            //mainWindow.TopMost = true;
            mainWindow.Show();
            mainWindow.WindowState = FormWindowState.Normal;

            mainWindow.Focus();
            mainWindow.BringToFront();

            mainWindow.SelectTab(2);

            this.Close();
        }
    }
}
