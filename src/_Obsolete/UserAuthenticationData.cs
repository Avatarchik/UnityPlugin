using System.Collections.Generic;

using Newtonsoft.Json;

using Application = UnityEngine.Application;
using Debug = UnityEngine.Debug;

namespace ModIO
{
    /// <summary>[Obsolete] A singleton struct that is referenced by multiple classes for user authentication.</summary>
    [System.Obsolete("Functionality can now be found in UserAccountManagement.")]
    [System.Serializable]
    public struct UserAuthenticationData
    {
        // ---------[ CONSTANTS ]---------
        /// <summary>An instance of UserAuthenticationData with zeroed fields.</summary>
        public static readonly UserAuthenticationData NONE = new UserAuthenticationData()
        {
            userId = UserProfile.NULL_ID,
            token = null,
            wasTokenRejected = false,
            steamTicket = null,
            gogTicket = null,
        };

        /// <summary>Location of the settings file.</summary>
        public static readonly string FILE_LOCATION = IOUtilities.CombinePath(PluginSettings.data.cacheDirectory,
                                                                              "user.data");

        // ---------[ FIELDS ]---------
        /// <summary>User Id associated with the stored OAuthToken.</summary>
        public int userId;

        /// <summary>User authentication token to send with API requests identifying the user.</summary>
        public string token;

        /// <summary>A flag to indicate that the auth token has been rejected.</summary>
        public bool wasTokenRejected;

        /// <summary>Steam ticket (if applicable).</summary>
        public string steamTicket;

        /// <summary>GOG ticket (if applicable).</summary>
        public string gogTicket;

        // --- ACCESSORS ---
        [JsonIgnore]
        public bool IsTokenValid
        { get { return !this.wasTokenRejected && !string.IsNullOrEmpty(this.token); } }

        // ---------[ SINGLETON ]---------
        /// <summary>Singleton instance to be used as the current/active data.</summary>
        public static UserAuthenticationData instance
        {
            get
            {
                LocalUser userData = UserAccountManagement.activeUser;
                UserProfile p = userData.profile;
                string steamTicket = null;
                string gogTicket = null;

                switch(userData.externalAuthTicket.provider)
                {
                    case ExternalAuthenticationProvider.Steam:
                    {
                        steamTicket = userData.externalAuthTicket.value;
                    }
                    break;

                    case ExternalAuthenticationProvider.GOG:
                    {
                        gogTicket = userData.externalAuthTicket.value;
                    }
                    break;
                }

                UserAuthenticationData data = new UserAuthenticationData()
                {
                    userId = (p == null ? UserProfile.NULL_ID : p.id),
                    token = userData.oAuthToken,
                    wasTokenRejected = userData.wasTokenRejected,
                    steamTicket = steamTicket,
                    gogTicket = gogTicket,
                };

                return data;
            }
            set
            {
                // get existing values
                List<int> enabled = UserAccountManagement.GetEnabledMods();
                List<int> subscribed = UserAccountManagement.GetSubscribedMods();

                // profile data
                UserProfile profile = UserAccountManagement.activeUser.profile;
                if(profile == null
                   || profile.id != value.userId)
                {
                    if(value.userId == UserProfile.NULL_ID)
                    {
                        profile = null;
                    }
                    else
                    {
                        profile = new UserProfile()
                        {
                            id = value.userId,
                        };
                    }
                }

                // externalAuthTicket data
                var ticket = new ExternalAuthenticationTicket()
                {
                    value = null,
                    provider = ExternalAuthenticationProvider.None,
                };

                if(!string.IsNullOrEmpty(value.steamTicket))
                {
                    ticket.value = value.steamTicket;
                    ticket.provider = ExternalAuthenticationProvider.Steam;
                }
                else if(!string.IsNullOrEmpty(value.gogTicket))
                {
                    ticket.value = value.gogTicket;
                    ticket.provider = ExternalAuthenticationProvider.GOG;
                }

                // create data
                LocalUser userData = new LocalUser()
                {
                    profile = profile,
                    oAuthToken = value.token,
                    wasTokenRejected = value.wasTokenRejected,
                    externalAuthTicket = ticket,
                    enabledModIds = enabled.ToArray(),
                    subscribedModIds = subscribed.ToArray(),
                };

                // set
                UserAccountManagement.SetLocalUserData(userData);
                UserAccountManagement.SaveActiveUser();
            }
        }

        // ---------[ SAVE/LOAD ]---------
        /// <summary>Clears the instance and deletes the data on disk.</summary>
        public static void Clear()
        {
            UserAuthenticationData.instance = UserAuthenticationData.NONE;
        }
    }
}