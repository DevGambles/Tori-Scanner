using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AVK_Scraper;
using System.Threading;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using System.Web;
using Newtonsoft.Json;

static class AvkScanner
{
    // Is thread for scanning already working?
    static bool alreadyScanning = false;

    // Variable used to inforrm thread for break scanning (used when user will change URL)
    static bool breakScanning = false;

    // URL to scan
    static string scanURL = String.Empty;

    // AJAX URL with actual cars
    static string ajaxURL = String.Empty;

    // Unique token used on requests
    static string token = String.Empty;

    // Refresh token?
    static bool refreshToken = true;

    // First scan completed? (to avoid email spam with old cars)
    static bool firstScanCompleted = false;

    // Scan auctions?
    static bool scanAuctions = false;

    // Scan direct sales?
    static bool scanDirectSales = false;

    public static void Start()
    {
        // Build new URL for today
        CreateScanURL();

        // Thread for scanning pages
        Thread scanThread = new Thread(new ThreadStart(ScanThread));
        scanThread.IsBackground = true;
        scanThread.Start();
    }

    /// <summary>
    /// Method for starting scan on AVK site
    /// </summary>
    public static void RefreshTarget(bool scanAuctions, bool scanDirectSales)
    {
        AvkScanner.scanAuctions = scanAuctions;
        AvkScanner.scanDirectSales = scanDirectSales;

        // If both empty
        if (!AvkScanner.scanAuctions && !AvkScanner.scanDirectSales) { AvkScanner.scanAuctions = AvkScanner.scanDirectSales = true; }

        // Create proper URL for today
        CreateScanURL();

        // Refresh token
        refreshToken = true;
    }

    static void CreateScanURL()
    {
        // Scan AVK - auctions + direct sales!
        if (scanAuctions && scanDirectSales)
        {
            scanURL = String.Format("https://www.avk.fi/Shop/Vehicle/VehicleSearchData?Query.FreeText=&Brand=0&Query.AskPriceMin=&Query.AskPriceMax=&Query.HighestBidMin=&Query.HighestBidMax=&Query.RegistrationYearMin=&Query.RegistrationYearMax=&Query.MeterReadoutMin=&Query.MeterReadoutMax=&Query.SaleDateMin={0}.{1}.{2}&Query.SaleDateMax={0}.{1}.{2}&Query.PrimeMover=&Query.TransmissionType=&Query.BodyModel=&Query.SaleTerm=&Query.IsAuctionTarget=#b",
                DateTime.Now.Day, DateTime.Now.Month, DateTime.Now.Year);
        }

        // Scan AVK - auctions
        else if (scanAuctions)
        {
            scanURL = String.Format("https://www.avk.fi/Shop/Vehicle/VehicleSearchData?Query.FreeText=&Brand=0&Query.AskPriceMin=&Query.AskPriceMax=&Query.HighestBidMin=&Query.HighestBidMax=&Query.RegistrationYearMin=&Query.RegistrationYearMax=&Query.MeterReadoutMin=&Query.MeterReadoutMax=&Query.SaleDateMin={0}.{1}.{2}&Query.SaleDateMax={0}.{1}.{2}&Query.PrimeMover=&Query.TransmissionType=&Query.BodyModel=&Query.SaleTerm=&Query.IsAuctionTarget=True#b",
                DateTime.Now.Day, DateTime.Now.Month, DateTime.Now.Year);
        }

        // Scan AVK - direct sales
        else if (scanDirectSales)
        {
            scanURL = String.Format("https://www.avk.fi/Shop/Vehicle/VehicleSearchData?Query.FreeText=&Brand=0&Query.AskPriceMin=&Query.AskPriceMax=&Query.HighestBidMin=&Query.HighestBidMax=&Query.RegistrationYearMin=&Query.RegistrationYearMax=&Query.MeterReadoutMin=&Query.MeterReadoutMax=&Query.SaleDateMin={0}.{1}.{2}&Query.SaleDateMax={0}.{1}.{2}&Query.PrimeMover=&Query.TransmissionType=&Query.BodyModel=&Query.SaleTerm=&Query.IsAuctionTarget=False#b",
                DateTime.Now.Day, DateTime.Now.Month, DateTime.Now.Year);
        }
    }

    static void ScanThread()
    {
        // Loop for scanning
        while (true)
        {
            // Wait for correct time
            if (DateTime.Now.TimeOfDay < Form1.scanTime_from.TimeOfDay)
            {
                // Sleep application to starting time
                Form1.actualStatus = String.Format("waiting to - {0}", Form1.scanTime_from.TimeOfDay.ToString());

                // Sleep
                Thread.Sleep(Form1.scanTime_from.TimeOfDay.Subtract(DateTime.Now.TimeOfDay));

                // Build new URL for today
                CreateScanURL();

                // Refresh token for today
                refreshToken = true;
            }

            // Check 'to' time
            if (DateTime.Now.TimeOfDay > Form1.scanTime_to.TimeOfDay)
            {
                // Status
                Form1.actualStatus = String.Format("today scanning completed");

                // Sleep 10min.
                Thread.Sleep(10 * 60 * 1000);

                // Loop again
                continue;
            }

            // Get AJAX URL + unique token? (when application will scan 1st time + when user will change source URL)
            if (refreshToken)
            {
                // Refresh token
                RefreshToken();

                // Check if token was created + URL
                if (!String.IsNullOrEmpty(ajaxURL) && !String.IsNullOrEmpty(token)) 
                {
                    refreshToken = false; 

                    // Next scan will be 1st
                    firstScanCompleted = false;
                }
            }

            // Proper scanning
            if (!String.IsNullOrEmpty(ajaxURL) && !String.IsNullOrEmpty(token))
            {
                // Status
                Form1.actualStatus = "extracting cars";

                // Extract cars
                ExtractNewCars();

                // First scan completed
                if (!firstScanCompleted) { firstScanCompleted = true; }
            }

            // Wait until next refresh
            Form1.actualStatus = "idle";
            Thread.Sleep(Form1.delaySec * 1000);
        }
    }

    static void RefreshToken()
    {
        string webResponse = null;
        int webTries = 0;

        while (webResponse == null && ++webTries <= 3)
        {
            HttpWebRequest webRequest = NetworkClass.CreateHttpAgent(scanURL, false);

            webResponse = NetworkClass.SendGetRequest(webRequest);
            if (webResponse == null) { continue; }

            HtmlAgilityPack.HtmlDocument resultPageDocument = new HtmlAgilityPack.HtmlDocument();
            resultPageDocument.LoadHtml(webResponse);

            // Get URL
            Match match_S = Regex.Match(webResponse, @"""sAjaxSource"": ""(.*?)""");
            if (match_S.Success)
            {
                ajaxURL = String.Format("https://www.avk.fi{0}", HttpUtility.HtmlDecode(match_S.Groups[1].Value)).Trim();
            }

            // Get Token
            HtmlNode tokenNode = resultPageDocument.DocumentNode.SelectSingleNode("//input[@name='__RequestVerificationToken' and @value]");
            if (tokenNode != null)
            {
                token = HttpUtility.HtmlDecode(tokenNode.Attributes["value"].Value).Trim();
            }
        }
    }

    static void ExtractNewCars()
    {
        string webResponse = null;
        int webTries = 0;

        StringBuilder postParameters = new StringBuilder();
        NetworkClass.AddPostParameter(ref postParameters, "sEcho", "3");
        NetworkClass.AddPostParameter(ref postParameters, "iColumns", "20");
        NetworkClass.AddPostParameter(ref postParameters, "sColumns", "");
        NetworkClass.AddPostParameter(ref postParameters, "iDisplayStart", "0");
        NetworkClass.AddPostParameter(ref postParameters, "iDisplayLength", "100");
        NetworkClass.AddPostParameter(ref postParameters, "mDataProp_0", "0");
        NetworkClass.AddPostParameter(ref postParameters, "mDataProp_1", "1");
        NetworkClass.AddPostParameter(ref postParameters, "mDataProp_2", "2");
        NetworkClass.AddPostParameter(ref postParameters, "mDataProp_3", "3");
        NetworkClass.AddPostParameter(ref postParameters, "mDataProp_4", "4");
        NetworkClass.AddPostParameter(ref postParameters, "mDataProp_5", "5");
        NetworkClass.AddPostParameter(ref postParameters, "mDataProp_6", "6");
        NetworkClass.AddPostParameter(ref postParameters, "mDataProp_7", "7");
        NetworkClass.AddPostParameter(ref postParameters, "mDataProp_8", "8");
        NetworkClass.AddPostParameter(ref postParameters, "mDataProp_9", "9");
        NetworkClass.AddPostParameter(ref postParameters, "mDataProp_10", "10");
        NetworkClass.AddPostParameter(ref postParameters, "mDataProp_11", "11");
        NetworkClass.AddPostParameter(ref postParameters, "mDataProp_12", "12");
        NetworkClass.AddPostParameter(ref postParameters, "mDataProp_13", "13");
        NetworkClass.AddPostParameter(ref postParameters, "mDataProp_14", "14");
        NetworkClass.AddPostParameter(ref postParameters, "mDataProp_15", "15");
        NetworkClass.AddPostParameter(ref postParameters, "mDataProp_16", "16");
        NetworkClass.AddPostParameter(ref postParameters, "mDataProp_17", "17");
        NetworkClass.AddPostParameter(ref postParameters, "mDataProp_18", "18");
        NetworkClass.AddPostParameter(ref postParameters, "mDataProp_19", "19");
        NetworkClass.AddPostParameter(ref postParameters, "iSortingCols", "0");
        NetworkClass.AddPostParameter(ref postParameters, "bSortable_0", "false");
        NetworkClass.AddPostParameter(ref postParameters, "bSortable_1", "true");
        NetworkClass.AddPostParameter(ref postParameters, "bSortable_2", "true");
        NetworkClass.AddPostParameter(ref postParameters, "bSortable_3", "true");
        NetworkClass.AddPostParameter(ref postParameters, "bSortable_4", "true");
        NetworkClass.AddPostParameter(ref postParameters, "bSortable_5", "true");
        NetworkClass.AddPostParameter(ref postParameters, "bSortable_6", "false");
        NetworkClass.AddPostParameter(ref postParameters, "bSortable_7", "true");
        NetworkClass.AddPostParameter(ref postParameters, "bSortable_8", "true");
        NetworkClass.AddPostParameter(ref postParameters, "bSortable_9", "true");
        NetworkClass.AddPostParameter(ref postParameters, "bSortable_10", "false");
        NetworkClass.AddPostParameter(ref postParameters, "bSortable_11", "true");
        NetworkClass.AddPostParameter(ref postParameters, "bSortable_12", "true");
        NetworkClass.AddPostParameter(ref postParameters, "bSortable_13", "true");
        NetworkClass.AddPostParameter(ref postParameters, "bSortable_14", "true");
        NetworkClass.AddPostParameter(ref postParameters, "bSortable_15", "true");
        NetworkClass.AddPostParameter(ref postParameters, "bSortable_16", "true");
        NetworkClass.AddPostParameter(ref postParameters, "bSortable_17", "true");
        NetworkClass.AddPostParameter(ref postParameters, "bSortable_18", "true");
        NetworkClass.AddPostParameter(ref postParameters, "bSortable_19", "true");
        NetworkClass.AddPostParameter(ref postParameters, "__RequestVerificationToken", token);

        while (webResponse == null && ++webTries <= 3)
        {
            HttpWebRequest webRequest = NetworkClass.CreateHttpAgent(ajaxURL, true);

            webResponse = NetworkClass.SendPostRequest(webRequest, NetworkClass.GetPostData(postParameters));
            if (webResponse == null) { continue; }

            try
            {
                JsonObj jsonResponse = JsonConvert.DeserializeObject<JsonObj>(webResponse);

                if (jsonResponse != null && jsonResponse.aaData.Count > 0)
                {
                    foreach (List<string> carInfoArray in jsonResponse.aaData)
                    {
                        // Skip last car (for testing)
                        //if (!firstScanCompleted && carInfoArray[1] == jsonResponse.aaData.LastOrDefault()[1]) { continue; }

                        Car newCar = new Car();
                        newCar.carID = HttpUtility.HtmlDecode(carInfoArray[1]).Trim();
                        newCar.carURL = String.Format("https://www.avk.fi{0}", HttpUtility.HtmlDecode(carInfoArray[17]).Trim());
                        //newCar.added = HttpUtility.HtmlDecode(carInfoArray[11]).Trim();

                        newCar.added = DateTime.Now.ToString();
                        newCar.name = HttpUtility.HtmlDecode(carInfoArray[18]).Trim();

                        if (!firstScanCompleted) { newCar.emailStatus = "skipped"; }
                        else { newCar.emailStatus = "on queue"; }

                        //Form1.CheckNewCarURL(newCar, !firstScanCompleted);
                    }
                }
                else
                    Console.WriteLine();
            }
            catch { }
        }
    }
}
