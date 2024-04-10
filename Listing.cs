using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public class Listing
{
    // ID
    public int ID = -1;

    // Scan URL used to find this listing
    public string fromScanURL = String.Empty;

    // Keywords used to find this listing
    public string fromKeywords = String.Empty;

    // URL
    public string URL = String.Empty;

    // Title
    public string title = String.Empty;

    // Keywords found
    public List<string> keywordsFound = new List<string>();

    // When added
    public string added = String.Empty;

    // Status of email notification
    public string emailStatus = String.Empty;

    // Hide on table?
    public bool hide = false;
}
