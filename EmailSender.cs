using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HigLabo.Net.Smtp;
using HigLabo.Mime;
using System.Threading;
using AVK_Scraper;
using System.Windows.Forms;

static class EmailSender
{
    // Port Number
    public static int portNumber = 993;

    // Use SSL/TLS?
    public static SmtpEncryptedCommunication communicationType = SmtpEncryptedCommunication.None;

    // User Name
    public static string userName = String.Empty;

    // Password
    public static string password = String.Empty;

    // Server Name
    public static string serverName = String.Empty;

    // From Address
    public static string fromAddress = String.Empty;

    // To Addresses
    public static List<string> toAddresses = new List<string>();

    // Queue with cars that should be send via email
    static List<Listing> listingsToEmail = new List<Listing>();

    public static void Start()
    {
        // Thread for scanning pages
        Thread emailThread = new Thread(new ThreadStart(EmailThread));
        emailThread.IsBackground = true;
        emailThread.Start();
    }

    public static void AddNewListing(Listing newListing)
    {
        lock (listingsToEmail)
        {
            listingsToEmail.Add(newListing);
        }
    }

    static public void SendTestEmail()
    {
        string mailResponse = SendEmail("Testing email from @Tori Scanner", "Hello, this is sample body message!");
        MessageBox.Show(String.Format("Result: {0}", mailResponse), "Email Test");
    }

    static string SendEmail(string title, string msgBody)
    {
        // Build information about status
        string status = String.Empty;

        try
        {
            using (SmtpClient smtpClient = new SmtpClient(serverName, portNumber, userName, password))
            {
                smtpClient.Port = portNumber;
                smtpClient.EncryptedCommunication = communicationType;

                if (!smtpClient.TryAuthenticate()) { return "authentication with email account failed"; }

                // Create mail message
                SmtpMessage smtpMessage = new SmtpMessage();
                smtpMessage.Subject = title;
                smtpMessage.BodyText = msgBody;

                // From email
                smtpMessage.From = new MailAddress(fromAddress);

                // Send to all emails
                foreach (string singleEmailStr in toAddresses) { smtpMessage.To.Add(new MailAddress(singleEmailStr)); }

                // Send message
                SendMailResult mailResult = smtpClient.SendMail(smtpMessage);

                // Something goes wrong ...
                if (!mailResult.SendSuccessful) { return String.Format("failed while sending (msg: {0} | state: {1})", mailResult.Message, mailResult.State); }
            }
        }
        catch (Exception ex)
        {
            return ex.Message.ToLower();
        }

        return "email sent";
    }

    static void EmailThread()
    {
        // Loop for sending email
        while (true)
        {
            // Nothing to send
            if (listingsToEmail.Count == 0)
            {
                // Sleep
                Thread.Sleep(1000);

                // Continue
                continue;
            }

            // Get car to email
            Listing listingToEmail = null;
            lock (listingsToEmail)
            {
                listingToEmail = listingsToEmail[0];
                listingsToEmail.RemoveAt(0);
            }

            // Update status + refresh grid on main window
            listingToEmail.emailStatus = "sending email ...";
            Form1.refreshGrid = true;

            // Build keyword string
            string allKeywords = listingToEmail.keywordsFound[0];
            foreach (string keyword in listingToEmail.keywordsFound.Skip(1)) { allKeywords += " | " + keyword; }

            // Build msg
            string msgTitle = String.Format("[TORI] New Listing: {0}", listingToEmail.title);
            string msgBody = String.Format("Keywords: {1}{0}Listing URL: {2}{0}Date: {3}", Environment.NewLine, allKeywords, listingToEmail.URL, listingToEmail.added);

            // Update status + refresh grid on main window
            listingToEmail.emailStatus = SendEmail(msgTitle, msgBody);
            Form1.refreshGrid = true;
        }
    }


}