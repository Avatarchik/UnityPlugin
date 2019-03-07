﻿using System.Collections.Generic;
using Newtonsoft.Json;

using DateTime = System.DateTime;
using Debug = UnityEngine.Debug;

#if UNITY_5_4_OR_NEWER
using UnityWebRequest = UnityEngine.Networking.UnityWebRequest;
#else
using UnityWebRequest = UnityEngine.Experimental.Networking.UnityWebRequest;
#endif

#if UNITY_2017_1_OR_NEWER
using DownloadHandlerFile = UnityEngine.Networking.DownloadHandlerFile;
#endif

namespace ModIO
{
    public class WebRequestError
    {
        // ---------[ NESTED CLASSES ]---------
        [System.Serializable]
        private class APIWrapper
        {
            [System.Serializable]
            public class APIError
            {
                public string message = null;
                public Dictionary<string, string> errors = null;
            }

            public APIError error = null;
        }

        // ---------[ FIELDS ]---------
        /// <summary>UnityWebRequest that generated the data for the error.</summary>
        public UnityWebRequest webRequest;

        /// <summary>The ServerTimeStamp at which the request was received.</summary>
        public int timeStamp;

        /// <summary>The message returned by the API explaining the error.</summary>
        public string errorMessage;

        /// <summary>Errors pertaining to specific POST data fields.</summary>
        public IDictionary<string, string> fieldValidationMessages;

        // - Interpreted Values -
        /// <summary>Indicates whether the provided authentication data was rejected.</summary>
        public bool isAuthenticationInvalid;

        /// <summary>Indicates whether the mod.io servers a unreachable (for whatever reason).</summary>
        public bool isServerUnreachable;

        /// <summary>Indicates whether this request will always fail for the provided data.</summary>
        public bool isRequestUnresolvable;

        /// <summary>Indicates whether the request triggered the Rate Limiter and when to retry.</summary>
        public int limitedUntilTimeStamp;

        /// <summary>A player/user-friendly message to display on the UI.</summary>
        public string displayMessage;

        // --- OBSOLETE FIELDS ---
        [System.Obsolete("Use webRequest.responseCode instead")]
        public int responseCode
        {
            get { return (webRequest != null ? (int)webRequest.responseCode : -1); }
        }
        [System.Obsolete("Use webRequest.method instead")]
        public string method
        {
            get { return (webRequest != null ? webRequest.method : "LOCAL"); }
        }
        [System.Obsolete("Use webRequest.url instead")]
        public string url
        {
            get { return (webRequest != null ? webRequest.url : string.Empty); }
        }
        [System.Obsolete("Use webRequest.GetResponseHeaders() instead")]
        public Dictionary<string, string> responseHeaders
        {
            get { return (webRequest != null ? webRequest.GetResponseHeaders() : null); }
        }

        [System.Obsolete("Use webRequest.downloadHandler.text instead")]
        public string responseBody
        {
            get
            {
                if(webRequest != null
                   && webRequest.downloadHandler != null
                   && !(webRequest.downloadHandler is DownloadHandlerFile))
                {
                    return webRequest.downloadHandler.text;
                }
                return string.Empty;
            }
        }

        /// <summary>[Obsolete] The message returned by the API explaining the error.</summary>
        [System.Obsolete("Use WebRequestError.errorMessage instead")]
        public string message
        {
            get { return this.errorMessage; }
            set { this.errorMessage = value; }
        }


        // ---------[ INITIALIZATION ]---------
        public static WebRequestError GenerateFromWebRequest(UnityWebRequest webRequest)
        {
            UnityEngine.Debug.Assert(webRequest != null);
            UnityEngine.Debug.Assert(webRequest.IsError());

            WebRequestError error = new WebRequestError();
            error.webRequest = webRequest;

            error.timeStamp = ParseDateHeaderAsTimeStamp(webRequest);

            error.ApplyAPIErrorValues();
            error.ApplyInterpretedValues();

            return error;
        }

        public static WebRequestError GenerateLocal(string errorMessage)
        {
            WebRequestError error = new WebRequestError()
            {
                webRequest = null,
                timeStamp = ServerTimeStamp.Now,
                errorMessage = errorMessage,
                displayMessage = errorMessage,

                isAuthenticationInvalid = false,
                isServerUnreachable = false,
                isRequestUnresolvable = false,
                limitedUntilTimeStamp = -1,
            };

            return error;
        }

        // ---------[ VALUE INTERPRETATION AND APPLICATION ]---------
        private static int ParseDateHeaderAsTimeStamp(UnityWebRequest webRequest)
        {
            var dateHeaderValue = webRequest.GetResponseHeader("Date");

            // Examples:
            //  Thu, 28 Feb 2019 07:04:38 GMT
            //  Fri, 01 Mar 2019 01:16:49 GMT
            string timeFormat = "ddd, dd MMM yyyy HH:mm:ss 'GMT'";
            DateTime time;

            if(!string.IsNullOrEmpty(dateHeaderValue)
               && DateTime.TryParseExact(dateHeaderValue,
                                         timeFormat,
                                         System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat,
                                         System.Globalization.DateTimeStyles.AssumeUniversal,
                                         out time))
            {
                // NOTE(@jackson): For some reason, System.Globalization.DateTimeStyles.AssumeUniversal
                //  is ignored(?) in TryParseExact, so it needs to be set as universal after the fact.
                time = DateTime.SpecifyKind(time, System.DateTimeKind.Utc);

                return ServerTimeStamp.FromUTCDateTime(time);
            }

            return ServerTimeStamp.Now;
        }

        private void ApplyAPIErrorValues()
        {
            this.errorMessage = null;
            this.fieldValidationMessages = null;

            // null-ref and type-check
            if(this.webRequest.downloadHandler != null
               && !(this.webRequest.downloadHandler is DownloadHandlerFile))
            {
                try
                {
                    // get the request content
                    string requestContent = this.webRequest.downloadHandler.text;
                    if(string.IsNullOrEmpty(requestContent)) { return; }

                    // deserialize into an APIError
                    WebRequestError.APIWrapper errorWrapper = JsonConvert.DeserializeObject<APIWrapper>(requestContent);
                    if(errorWrapper == null
                       || errorWrapper.error == null)
                    {
                        return;
                    }

                    // extract values
                    this.errorMessage = errorWrapper.error.message;
                    this.fieldValidationMessages = errorWrapper.error.errors;
                }
                catch(System.Exception e)
                {
                    Debug.LogWarning("[mod.io] Error deserializing API Error:\n"
                                     + e.Message);
                }
            }

            if(this.errorMessage == null)
            {
                this.errorMessage = this.webRequest.error;
            }
        }

        private void ApplyInterpretedValues()
        {
            this.isAuthenticationInvalid = false;
            this.isServerUnreachable = false;
            this.isRequestUnresolvable = false;
            this.limitedUntilTimeStamp = -1;
            this.displayMessage = string.Empty;

            if(this.webRequest == null) { return; }

            // Interpret code
            switch(this.webRequest.responseCode)
            {
                // - Generic coding errors -
                // Bad Request
                case 400:
                // Method Not Allowed
                case 405:
                // Not Acceptable
                case 406:
                // Unsupported Media Type
                case 415:
                {
                    this.displayMessage = ("Error synchronizing with the mod.io servers.");

                    this.isRequestUnresolvable = true;

                    Debug.LogWarning("[mod.io] " + this.ToUnityDebugString());
                }
                break;

                // Bad authorization
                case 401:
                {
                    this.displayMessage = ("Your mod.io authentication details have changed."
                                           + "\nLogging out and in again should correct this issue.");

                    this.isAuthenticationInvalid = true;
                    this.isRequestUnresolvable = true;
                }
                break;

                // Forbidden
                case 403:
                {
                    this.displayMessage = ("Your account does not have the required permissions.");

                    this.isRequestUnresolvable = true;
                }
                break;

                // Not found
                case 404:
                // Gone
                case 410:
                {
                    this.displayMessage = ("A mod.io networking error was encountered."
                                           + "\nPlease report this as [" + this.webRequest.responseCode.ToString()
                                           + "@" + this.webRequest.url + "] to jackson@mod.io");

                    this.isRequestUnresolvable = true;
                }
                break;

                // case 405: Handled Above
                // case 406: Handled Above

                // Timeout
                case 408:
                {
                    this.displayMessage = ("The mod.io servers cannot be reached."
                                           + "\nPlease check your internet connection.");

                    this.isServerUnreachable = true;
                }
                break;

                // case 410: Handled Above

                // Unprocessable Entity
                case 422:
                {
                    var displayString = new System.Text.StringBuilder();
                    displayString.AppendLine("The submitted data contained error(s).");

                    if(this.fieldValidationMessages != null
                       && this.fieldValidationMessages.Count > 0)
                    {
                        foreach(var kvp in fieldValidationMessages)
                        {
                            displayString.AppendLine("- [" + kvp.Key + "] " + kvp.Value);
                        }
                    }

                    if(displayString.Length > 0
                       && displayString[displayString.Length - 1] == '\n')
                    {
                        --displayString.Length;
                    }

                    this.displayMessage = displayString.ToString();

                    this.isRequestUnresolvable = true;
                }
                break;

                // Too Many Requests
                case 429:
                {
                    string retryAfterString;
                    int retryAfterSeconds;

                    var responseHeaders = this.webRequest.GetResponseHeaders();
                    if(!(responseHeaders.TryGetValue("X-Ratelimit-RetryAfter", out retryAfterString)
                         && int.TryParse(retryAfterString, out retryAfterSeconds)))
                    {
                        retryAfterSeconds = 60;

                        Debug.LogWarning("[mod.io] Too many APIRequests have been made, however"
                                         + " no valid X-Ratelimit-RetryAfter header was detected."
                                         + "\nPlease report this to jackson@mod.io with the following information:"
                                         + "\n[" + this.webRequest.url
                                         + ":" + this.webRequest.method
                                         + "-" + this.errorMessage + "]");
                    }

                    this.displayMessage = ("Too many requests have been made to the mod.io servers."
                                           + "\nReconnecting in " + retryAfterSeconds.ToString() + " seconds.");

                    this.limitedUntilTimeStamp = this.timeStamp + retryAfterSeconds;
                }
                break;

                // Internal server error
                case 500:
                {
                    this.displayMessage = ("There was an error with the mod.io servers. Staff have been"
                                           + " notified, and will attempt to fix the issue as soon as possible.");

                    this.isRequestUnresolvable = true;
                }
                break;

                // Service Unavailable
                case 503:
                {
                    this.displayMessage = "The mod.io servers are currently offline.";

                    this.isServerUnreachable = true;
                }
                break;

                default:
                {
                    // Cannot connect resolve destination host, used by Unity
                    if(this.webRequest.responseCode <= 0)
                    {
                        this.displayMessage = ("The mod.io servers cannot be reached."
                                               + "\nPlease check your internet connection.");

                        this.isServerUnreachable = true;
                    }
                    else
                    {
                        this.displayMessage = ("Error synchronizing with the mod.io servers.");

                        this.isRequestUnresolvable = true;

                        Debug.LogWarning("[mod.io] An unhandled error was returned during a web request."
                                         + "\nPlease report this to jackson@mod.io with the following information:\n"
                                         + this.ToUnityDebugString());
                    }
                }
                break;
            }
        }

        // ---------[ HELPER FUNCTIONS ]---------
        public string ToUnityDebugString()
        {
            var debugString = new System.Text.StringBuilder();

            string headerString = (this.webRequest == null ? "REQUEST FAILED LOCALLY"
                                   : this.webRequest.method.ToUpper() + " REQUEST FAILED");
            debugString.AppendLine(headerString);

            if(this.webRequest != null)
            {
                debugString.AppendLine("URL: " + this.webRequest.url);
                debugString.AppendLine("TimeStamp: " + this.timeStamp + " ("
                                       + ServerTimeStamp.ToLocalDateTime(this.timeStamp) + ")");
                debugString.AppendLine("ResponseCode: " + this.webRequest.responseCode.ToString());

                var responseHeaders = webRequest.GetResponseHeaders();
                if(responseHeaders != null
                   && responseHeaders.Count > 0)
                {
                    debugString.AppendLine("Response Headers:");
                    foreach(var kvp in responseHeaders)
                    {
                        debugString.AppendLine("- [" + kvp.Key + "] " + kvp.Value);
                    }
                }

                debugString.AppendLine("ErrorMessage: " + this.errorMessage);

                if(this.fieldValidationMessages != null
                   && this.fieldValidationMessages.Count > 0)
                {
                    debugString.AppendLine("Field Validation Messages:");
                    foreach(var kvp in fieldValidationMessages)
                    {
                        debugString.AppendLine("- [" + kvp.Key + "] " + kvp.Value);
                    }
                }

                debugString.AppendLine(".isAuthenticationInvalid = "    + this.isAuthenticationInvalid.ToString());
                debugString.AppendLine(".isServerUnreachable = "        + this.isServerUnreachable.ToString());
                debugString.AppendLine(".isRequestUnresolvable = "      + this.isRequestUnresolvable.ToString());
                debugString.AppendLine(".limitedUntilTimeStamp = "      + this.limitedUntilTimeStamp.ToString());
                debugString.AppendLine(".displayMessage = "             + this.displayMessage);
            }

            return debugString.ToString();
        }

        public static void LogAsWarning(WebRequestError error)
        {
            Debug.LogWarning(error.ToUnityDebugString());
        }
    }
}
