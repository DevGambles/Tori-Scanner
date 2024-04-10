using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

class ScanSettings
{
    // URL to scan
    public string scanURL = String.Empty;

    // Keywords to check
    public List<string> keywords = new List<string>();

    // Founded listing ID's (ID = listing URL + listing Title (everything lowered))
    public List<string> alreadyExtractedListings = new List<string>();

    public string GetKeywordsString()
    {
        string keywords_string = keywords.Count > 0 ? keywords[0] : String.Empty;

        foreach (string keyword in keywords.Skip(1)) { keywords_string += "," + keyword; }

        return keywords_string;
    }

    public string GetID()
    {
        return String.Format("{0}{1}", scanURL.ToLower(), GetKeywordsString().ToLower());
    }
}
