using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Threading;
using System.IO;
using System.Xml;
using System.Web;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Collections;
using System.Runtime.Serialization.Formatters.Binary;

static class NetworkClass
{
    // Locker
    static Object locker_ = new Object();

    // Min-Max connections
    public static int connectionActive = 0;
    public static int connectionMax = 1; // 8

    // Timeout
    public static int timeoutSec = 30;

    // Proxy use?
    public static bool useProxy = false;

    // User agents
    static List<string> userAgents = new List<string>()
        {
        "Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/36.0.1985.143 Safari/537.36",
        "Mozilla/5.0 (Windows NT 6.1; WOW64; rv:31.0) Gecko/20100101 Firefox/31.0",
        "Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/36.0.1985.125 Safari/537.36",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_9_4) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/36.0.1985.143 Safari/537.36",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_9_4) AppleWebKit/537.78.2 (KHTML, like Gecko) Version/7.0.6 Safari/537.78.2",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_9_4) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/36.0.1985.125 Safari/537.36",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10.9; rv:31.0) Gecko/20100101 Firefox/31.0",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_9_4) AppleWebKit/537.77.4 (KHTML, like Gecko) Version/7.0.5 Safari/537.77.4",
        "Mozilla/5.0 (Windows NT 6.3; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/36.0.1985.143 Safari/537.36",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_9_4) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/37.0.2062.94 Safari/537.36",
        "Mozilla/5.0 (Windows NT 6.3; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/36.0.1985.125 Safari/537.36",
        "Mozilla/5.0 (Windows NT 6.3; WOW64; rv:31.0) Gecko/20100101 Firefox/31.0",
        "Mozilla/5.0 (Windows NT 6.1; WOW64; Trident/7.0; rv:11.0) like Gecko",
        "Mozilla/5.0 (Windows NT 6.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/36.0.1985.143 Safari/537.36",
        "Mozilla/5.0 (X11; Ubuntu; Linux x86_64; rv:31.0) Gecko/20100101 Firefox/31.0",
        "Mozilla/5.0 (Windows NT 6.1; rv:31.0) Gecko/20100101 Firefox/31.0",
        "Mozilla/5.0 (iPad; CPU OS 7_1_2 like Mac OS X) AppleWebKit/537.51.2 (KHTML, like Gecko) Version/7.0 Mobile/11D257 Safari/9537.53",
        "Mozilla/5.0 (Windows NT 6.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/36.0.1985.125 Safari/537.36",
        "Mozilla/5.0 (Windows NT 5.1; rv:31.0) Gecko/20100101 Firefox/31.0"
        };

    // Radom
    static Random rand = new Random();

    // Cookies
    public static CookieContainer cookies = new CookieContainer();

    static public HttpWebRequest CreateHttpAgent(string requestUrl, bool postType)
    {
        HttpWebRequest httpRequest = (HttpWebRequest)WebRequest.Create(requestUrl);
        httpRequest.CookieContainer = cookies;

        if (postType)
        {
            httpRequest.Method = "POST";
            httpRequest.ContentType = "application/x-www-form-urlencoded";
        }

        httpRequest.UserAgent = userAgents[rand.Next(0, userAgents.Count)];
        httpRequest.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
        httpRequest.Headers.Set("Accept-Language", "en-us;q=0.7,en;q=0.3");
        httpRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        httpRequest.KeepAlive = true;
        httpRequest.ServicePoint.Expect100Continue = false;

        httpRequest.Timeout = timeoutSec * 1000;
        httpRequest.ReadWriteTimeout = timeoutSec * 1000;

        return httpRequest;
    }

    static public string SendGetRequest(HttpWebRequest httpRequest)
    {
        string resultPage = null;

        try
        {
            using (HttpWebResponse webResponse = (HttpWebResponse)httpRequest.GetResponse())
            {
                string characterSet = webResponse.CharacterSet;

                using (Stream responseStream = webResponse.GetResponseStream())
                {
                    using (StreamReader streamReader = new StreamReader(responseStream, Encoding.GetEncoding(characterSet)))
                    {
                        resultPage = streamReader.ReadToEnd();
                    }
                }
            }
        }
        catch (Exception ex_) { System.Console.WriteLine(ex_.Message); }

        return resultPage;
    }

    static public void AddPostParameter(ref StringBuilder postString, string key, string value)
    {
        if (postString.Length > 0)
            postString.Append("&");

        postString.AppendFormat("{0}={1}", HttpUtility.UrlEncode(key), HttpUtility.UrlEncode(value));
    }

    static public byte[] GetPostData(StringBuilder postString)
    {
        return Encoding.ASCII.GetBytes(postString.ToString());
    }

    static public string SendPostRequest(HttpWebRequest httpRequest, byte[] postData)
    {
        httpRequest.ContentLength = postData.Length;

        string resultPage = null;

        try
        {
            using (Stream requestStream = httpRequest.GetRequestStream())
            {
                requestStream.Write(postData, 0, postData.Length);
            }

            using (HttpWebResponse webResponse = (HttpWebResponse)httpRequest.GetResponse())
            {
                using (Stream responseStream = webResponse.GetResponseStream())
                {
                    using (StreamReader streamReader = new StreamReader(responseStream))
                    {
                        resultPage = streamReader.ReadToEnd();
                    }
                }
            }
        }
        catch (Exception ex_) { System.Console.WriteLine(ex_.Message); }

        return resultPage;
    }
    public static string GetResponseString(HttpWebRequest request)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        StringBuilder sb = new StringBuilder();
        Stream resStream = null;
        HttpWebResponse response = null;
        byte[] buf = new byte[8192];

        try
        {
            // execute the request
            response = (HttpWebResponse)request.GetResponse();

            // we will read data via the response stream
            resStream = response.GetResponseStream();
            string tempString = null;
            int count = 0;
            do
            {
                // fill the buffer with data
                count = resStream.Read(buf, 0, buf.Length);
                // make sure we read some data
                if (count != 0)
                {
                    // translate from bytes to ASCII text
                    tempString = Encoding.ASCII.GetString(buf, 0, count);
                    // continue building the string
                    sb.Append(tempString);
                }
            }
            while (count > 0); // any more data to read?
        }
        catch (Exception err)
        {
            String exc = err.Message;
        }
        finally
        {
            if (response != null) response.Close();
            if (resStream != null) resStream.Close();
        }

        return sb.ToString();
    }
}