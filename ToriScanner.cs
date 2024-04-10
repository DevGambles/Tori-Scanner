using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using AVK_Scraper;
using System.Net;
using HtmlAgilityPack;
using System.Web;
using System.IO;

class ToriScanner
{
    // All target URL's to scan
    //public static List<string> scanURLs = new List<string>();

    // All keywords to check
    //public static List<string> keywords = new List<string>();

    // Founded listing ID's by scan URL's (ID = listing URL + listing Title (everything lowered))
    //static Dictionary<string, List<string>> alreadyExtractedIDsByScanURL = new Dictionary<string, List<string>>();

    // All scan settings
    public static Dictionary<string, ScanSettings> scanSettingsByID = new Dictionary<string, ScanSettings>();

    public static void Start()
    {
        // Thread for scanning pages
        Thread scanThread = new Thread(new ThreadStart(ScanThread));
        scanThread.IsBackground = true;
        scanThread.Start();
    }

    static void ScanThread()
    {
        // Prepare lists with scan urls + keywords (to avoid crash when user will change them on other thread)
        List<ScanSettings> scanSettingsToCheck = null;

        // Loop for scanning
        while (true)
        {
            // The starting time must be earlier than the ending time!
            if (Form1.scanTime_from >= Form1.scanTime_to)
            {
                // Sleep application to starting time
                Form1.actualStatus = String.Format("the starting time must be earlier than the ending time ...", Form1.scanTime_from.TimeOfDay.ToString());

                // Sleep 10 sec.
                Thread.Sleep(10 * 1000);

                // Loop again (to check if user changed from-to time)
                continue;
            }

            // Wait for correct time
            if (DateTime.Now.TimeOfDay < Form1.scanTime_from.TimeOfDay)
            {
                // Sleep application to starting time
                Form1.actualStatus = String.Format("waiting to - {0}", Form1.scanTime_from.TimeOfDay.ToString());

                // Starting again so mark next scans as first
                lock (scanSettingsByID.Values)
                {
                    foreach (ScanSettings scanSetting in scanSettingsByID.Values) { scanSetting.alreadyExtractedListings.Clear(); }
                }

                // Sleep 10 sec.
                Thread.Sleep(10 * 1000);

                // Loop again (to check if user changed from-to time)
                continue;
            }

            // Check 'to' time
            if (DateTime.Now.TimeOfDay > Form1.scanTime_to.TimeOfDay)
            {
                // Status
                Form1.actualStatus = String.Format("today's scan completed");

                // Sleep 10 sec.
                Thread.Sleep(10 * 1000);

                // Loop again (to check if user changed from-to time)
                continue;
            }

            // Copy ScanSettings to check
            lock (scanSettingsByID.Values)
            {
                scanSettingsToCheck = new List<ScanSettings>(scanSettingsByID.Values);
            }

            // Update Status
            Form1.actualStatus = "checking new listings";

            // Check each scan setting
            foreach (ScanSettings scanSettingToCheck in scanSettingsToCheck)
            {
                // Check each keyword
                CheckScanSetting(scanSettingToCheck);
            }

            // Wait until next refresh
            Form1.actualStatus = String.Format("waiting {0} sec.", Form1.delaySec);
            Thread.Sleep(Form1.delaySec * 1000);
        }
    }

    static void CheckScanSetting(ScanSettings scanSettingToCheck_)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        // Request vars
        string webResponse = null;
        int webTries = 0;

        // Max 5 tries ...
        while (webResponse == null && ++webTries <= 3)
        {
            // Main request var
            //HttpWebRequest webRequest = NetworkClass.CreateHttpAgent(scanSettingToCheck_.scanURL, false);
            // Send request
            //webResponse = NetworkClass.SendGetRequest(webRequest);
            WebRequest webRequest = WebRequest.Create(scanSettingToCheck_.scanURL);
            webResponse = NetworkClass.GetResponseString((HttpWebRequest)webRequest);
            if (webResponse == null) { continue; }

            // Load HTML code page
            HtmlAgilityPack.HtmlDocument htmlDocument = new HtmlAgilityPack.HtmlDocument();
            htmlDocument.LoadHtml(webResponse);

            // It's first listing for this scan URL?
            bool firstScan = scanSettingToCheck_.alreadyExtractedListings.Count == 0;

            // In some case url might have 0 listings so to prevent from infinite 'first scan' add some string into dic (just to prevent 'first scan' in future!)
            if (firstScan) { scanSettingToCheck_.alreadyExtractedListings.Add("first_scan_completed"); }

            // All new listings
            List<Listing> newListings = new List<Listing>();

            // Check each listing (not premium one)
            HtmlNodeCollection listingNodeCol = htmlDocument.DocumentNode.SelectNodes("//*[contains(@id,'item_') and not(contains(@id,'pp_')) and @href]");
            if (listingNodeCol != null)
            {
                // Check each one
                foreach (HtmlNode listingNode in listingNodeCol)
                {
                    // Listing URL
                    string listingURL = HttpUtility.HtmlDecode(listingNode.Attributes["href"].Value).Trim();

                    // Listing Title
                    HtmlNode titleNode = listingNode.SelectSingleNode("./div[@class='li-title']/*[@class='li-title']");
                    if (titleNode == null) { titleNode = listingNode.SelectSingleNode(".//div[@class='li-title']"); }
                    if (titleNode != null)
                    {
                        // Set title!
                        string listingTitle = HttpUtility.HtmlDecode(titleNode.InnerText).Trim();

                        // Build ID
                        string ID = String.Format("{0}{1}", listingURL, listingTitle).ToLower().Trim();

                        // If already exist on DB then check next listing
                        if (scanSettingToCheck_.alreadyExtractedListings.Contains(ID)) { continue; }

                        // Add url to DB
                        scanSettingToCheck_.alreadyExtractedListings.Add(ID);

                        // Extract details from listing URL
                        Listing listing = ExtractListingDetails(listingURL, scanSettingToCheck_.keywords);

                        // Not null?
                        if (listing != null)
                        {
                            // Mark scan URL
                            listing.fromScanURL = scanSettingToCheck_.scanURL;

                            // Mark keywords
                            listing.fromKeywords = scanSettingToCheck_.GetKeywordsString();

                            // Add into list with all new listings
                            newListings.Insert(0, listing);
                        }

                        // Error?
                        else { Console.WriteLine(); }
                    }
                }
            }

            // Check every new listing
            foreach (Listing newListing in newListings)
            {
                // Email status
                if (firstScan) { newListing.emailStatus = "skipped (first scan)"; }
                else if (newListing.keywordsFound.Count == 0) { newListing.emailStatus = "skipped (no keywords found)"; }
                else { newListing.emailStatus = "on queue"; }

                // Set actual date
                newListing.added = DateTime.Now.ToString();

                // Send to global DB and show on table
                Form1.AddNewListing(newListing, firstScan);
            }
        }
    }

    static Listing ExtractListingDetails(string listingURL, List<string> keywordsToCheck)
    {
        // Var with listing
        Listing listingObj = null;

        // Request vars
        string webResponse = null;
        int webTries = 0;

        // Max 5 tries ...
        while (webResponse == null && ++webTries <= 3)
        {
            // Main request var
            HttpWebRequest webRequest = NetworkClass.CreateHttpAgent(listingURL, false);

            // Send request
            webResponse = NetworkClass.SendGetRequest(webRequest);
            if (webResponse == null) { continue; }

            // Load HTML code page
            HtmlAgilityPack.HtmlDocument htmlDocument = new HtmlAgilityPack.HtmlDocument();
            htmlDocument.LoadHtml(webResponse);

            // Create obj
            listingObj = new Listing();
            listingObj.URL = listingURL;

            // Title
            HtmlNode tmpNode = htmlDocument.DocumentNode.SelectSingleNode("//h1[@itemprop='name']");
            if (tmpNode != null) { listingObj.title = HttpUtility.HtmlDecode(tmpNode.InnerText).Trim(); }

            // Description
            string description = String.Empty;
            tmpNode = htmlDocument.DocumentNode.SelectSingleNode("//div[@itemprop='description']");
            if (tmpNode != null) { description = HttpUtility.HtmlDecode(tmpNode.InnerText).Trim(); }

            // Compate title + desc with each keyword
            foreach (string keywordToCheck in keywordsToCheck)
            {
                if (listingObj.title.ToLower().Contains(keywordToCheck.ToLower()) || description.ToLower().Contains(keywordToCheck.ToLower()))
                {
                    // Add keyword to obj
                    listingObj.keywordsFound.Add(keywordToCheck);
                }
            }
        }

        // Return listing OBJ
        return listingObj;
    }






}
