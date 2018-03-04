﻿#define USING_TEST_SERVER
// #define TEST_IGNORE_DISK_CACHE

using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace ModIO
{
    public delegate void ModEventHandler(ModInfo mod);
    public delegate void ModIDEventHandler(int modId);
    public delegate void ModfileEventHandler(int modId, Modfile newModfile);
    public delegate void ModLogoEventHandler(int modId, Texture2D modLogo, LogoVersion logoVersion);

    public enum ModBinaryStatus
    {
        Missing,
        RequiresUpdate,
        UpToDate
    }

    public class ModManager : MonoBehaviour
    {
        private static ModManager instance = null;

        private Action requestHandlerUpdateHandle = null;
        private void Update()
        {
            if(requestHandlerUpdateHandle != null)
            {
                requestHandlerUpdateHandle();
            }
        }

        // ---------[ INNER CLASSES ]---------
        [System.Serializable]
        private class UserData
        {
            public string oAuthToken = "";
            public User user = null;
            public List<int> subscribedModIDs = new List<int>();
        }

        [System.Serializable]
        private class ManifestData
        {
            public TimeStamp lastUpdateTimeStamp;
            public List<ModEvent> unresolvedEvents;
            public GameInfo game;
        }

        // ---------[ VARIABLES ]---------
        private static ManifestData manifest = null;
        private static UserData userData = null;

        #if USING_TEST_SERVER
        public static string MODIO_DIR { get { return Application.persistentDataPath + "/modio_testServer/"; } }
        #else
        public static string MODIO_DIR { get { return Application.persistentDataPath + "/modio/"; } }
        #endif

        private static string MANIFEST_URL { get { return MODIO_DIR + "manifest.data"; } }
        private static string USERDATA_URL { get { return MODIO_DIR + "user.data"; } }
        
        // public static APIClient apiClient { get { return client; } }
        public static User currentUser { get { return userData == null ? null : userData.user; } }
        public static GameInfo gameInfo { get { return manifest.game; }}

        // --------- [ INITIALIZATION ]---------
        public static void Initialize()
        {
            if(manifest != null)
            {
                return;
            }

            Debug.Log("Initializing ModIO.ModManager"
                      + "\nModIO Directory: " + MODIO_DIR);

            // TODO(@jackson): Listen to logo update for caching

            LoadCacheFromDisk();
            FetchAndCacheAllMods();
            FetchAndCacheGameInfo();
        }

        private static void LoadCacheFromDisk()
        {
            if (!Directory.Exists(MODIO_DIR))
            {
                Directory.CreateDirectory(MODIO_DIR);
            }

            #if TEST_IGNORE_DISK_CACHE
            {
                manifest = new ManifestData();
                manifest.lastUpdateTimeStamp = new TimeStamp();
                manifest.unresolvedEvents = new List<ModEvent>();

                WriteManifestToDisk();
            }
            #else
            {
                if(!File.Exists(MANIFEST_URL))
                {
                    // --- INITIALIZE FIRST RUN ---
                    manifest = new ManifestData();
                    manifest.lastUpdateTimeStamp = new TimeStamp();
                    manifest.unresolvedEvents = new List<ModEvent>();

                    WriteManifestToDisk();
                }
                else
                {
                    manifest = JsonUtility.FromJson<ManifestData>(File.ReadAllText(MANIFEST_URL));
                }


                // iterate through folders, load ModInfo
                if(!Directory.Exists(MODIO_DIR + "mods/"))
                {
                    Directory.CreateDirectory(MODIO_DIR + "mods/");
                }

                string[] modDirectories = Directory.GetDirectories(MODIO_DIR + "mods/");
                foreach(string modDir in modDirectories)
                {
                    // Load ModInfo from Disk
                    ModInfo mod = JsonUtility.FromJson<ModInfo>(File.ReadAllText(modDir + "/mod.data"));
                    modCache.Add(mod.id, mod);
                }

                // Attempt to load user
                if(File.Exists(USERDATA_URL))
                {
                    userData = JsonUtility.FromJson<UserData>(File.ReadAllText(USERDATA_URL));

                    Action<ErrorInfo> onAuthenticationFail = (error) =>
                    {
                        if(error.httpStatusCode == 401
                            || error.httpStatusCode == 403) // Failed authentication
                        {
                            LogUserOut();
                        }
                    };

                    APIClient.GetAuthenticatedUser(userData.oAuthToken,
                                                   APIClient.IgnoreResponse,
                                                   onAuthenticationFail);
                }
            }
            #endif
        }

        private static void FetchAndCacheAllMods()
        {
            Action<ModInfo[]> AddModsToCache = (mods) =>
            {
                // TODO(@jackson): Implement mod is unavailable
                // TODO(@jackson): Check for modfile change

                manifest.lastUpdateTimeStamp = TimeStamp.Now();
                WriteManifestToDisk();

                List<ModInfo> updatedMods = new List<ModInfo>();
                List<ModInfo> addedMods = new List<ModInfo>();

                foreach(ModInfo mod in mods)
                {
                    ModInfo cachedMod;
                    if(modCache.TryGetValue(mod.id, out cachedMod)
                       && !cachedMod.Equals(mod))
                    {
                        CacheMod(mod);
                        updatedMods.Add(mod);
                    }
                    else
                    {
                        CacheMod(mod);
                        addedMods.Add(mod);
                    }
                }

                if(OnModAdded != null)
                {
                    foreach(ModInfo mod in addedMods)
                    {
                        OnModAdded(mod);
                    }
                }

                if(OnModUpdated != null)
                {
                    foreach(ModInfo mod in updatedMods)
                    {
                        OnModUpdated(mod.id);
                    }
                }
            };

            APIClient.GetAllMods(GetAllModsFilter.None,
                                 AddModsToCache,
                                 APIClient.LogError);
        }

        private static void FetchAndCacheGameInfo()
        {
            Action<GameInfo> cacheGameInfo = (game) =>
            {
                manifest.game = game;
                WriteManifestToDisk();
            };

            APIClient.GetGame(cacheGameInfo, APIClient.IgnoreResponse);
        }

        // ---------[ AUTOMATED UPDATING ]---------
        private const float SECONDS_BETWEEN_POLLING = 15.0f;
        private static bool isUpdatePollingEnabled = false;
        private static bool isUpdatePollingRunning = false;

        public static void EnableUpdatePolling()
        {
            isUpdatePollingEnabled = true;
            
            if(!isUpdatePollingRunning)
            {
                instance.StartCoroutine(PollForUpdates());
            }
        }
        public static void DisableUpdatePolling()
        {
            isUpdatePollingEnabled = false;
        }

        private static IEnumerator PollForUpdates()
        {
            if(isUpdatePollingEnabled)
            {
                isUpdatePollingRunning = true;
                
                TimeStamp fromTimeStamp = manifest.lastUpdateTimeStamp;
                TimeStamp untilTimeStamp = TimeStamp.Now();

                // - Get Game Updates -
                FetchAndCacheGameInfo();

                // - Get ModInfo Events -
                GetAllModEventsFilter eventFilter = new GetAllModEventsFilter();
                eventFilter.ApplyIntRange(GetAllModEventsFilter.Field.DateAdded,
                                          fromTimeStamp.AsServerTimeStamp(), true,
                                          untilTimeStamp.AsServerTimeStamp(), false);
                eventFilter.ApplyBooleanIs(GetAllModEventsFilter.Field.Latest,
                                           true);

                Action<ModEvent[]> onModEventsReceived = (eventArray) =>
                {
                    manifest.lastUpdateTimeStamp = untilTimeStamp;
                    ProcessModEvents(eventArray);
                };
                APIClient.GetAllModEvents(eventFilter,
                                          onModEventsReceived,
                                          APIClient.LogError);

                // TODO(@jackson): Replace with Event Polling
                // - Get Subscription Updates -
                if(userData != null)
                {
                    GetUserSubscriptionsFilter subscriptionFilter = new GetUserSubscriptionsFilter();
                    subscriptionFilter.ApplyIntEquality(GetUserSubscriptionsFilter.Field.GameId, GlobalSettings.GAME_ID);

                    APIClient.GetUserSubscriptions(userData.oAuthToken,
                                                   subscriptionFilter,
                                                   UpdateSubscriptions,
                                                   APIClient.LogError);
                }

                yield return new WaitForSeconds(SECONDS_BETWEEN_POLLING);
                isUpdatePollingRunning = false;

                instance.StartCoroutine(PollForUpdates());
            }
        }

        private static void ProcessModEvents(ModEvent[] eventArray)
        {
            // - ModInfo Processing Options -
            Action<ModEvent> processModAvailable = (modEvent) =>
            {
                Action<ModInfo> onGetMod = (mod) =>
                {
                    CacheMod(mod);
                    manifest.unresolvedEvents.Remove(modEvent);

                    if(OnModAdded != null)
                    {
                        OnModAdded(mod);
                    }
                };

                APIClient.GetMod(modEvent.modId,
                                 onGetMod,
                                 APIClient.LogError);
            };
            Action<ModEvent> processModUnavailable = (modEvent) =>
            {
                // TODO(@jackson): Facilitate marking Mods as installed
                bool isModInstalled = (userData != null
                                       && userData.subscribedModIDs.Contains(modEvent.modId));

                if(!isModInstalled
                   && modCache.ContainsKey(modEvent.modId))
                {
                    UncacheMod(modEvent.modId);

                    if(OnModRemoved != null)
                    {
                        OnModRemoved(modEvent.modId);
                    }
                }
                manifest.unresolvedEvents.Remove(modEvent);
            };

            Action<ModEvent> processModEdited = (modEvent) =>
            {
                Action<ModInfo> onGetMod = (mod) =>
                {
                    CacheMod(mod);
                    manifest.unresolvedEvents.Remove(modEvent);

                    if(OnModUpdated != null)
                    {
                        OnModUpdated(mod.id);
                    }
                };
                APIClient.GetMod(modEvent.modId, onGetMod, APIClient.LogError);
            };


            Action<ModEvent> processModfileChange = (modEvent) =>
            {
                ModInfo mod = GetMod(modEvent.modId);

                if(mod == null)
                {
                    Debug.Log("Received Modfile change for uncached mod. Ignoring.");
                    manifest.unresolvedEvents.Remove(modEvent);
                }
                else
                {
                    Action<ModInfo> onGetMod = (updatedMod) =>
                    {
                        CacheMod(updatedMod);

                        if(OnModfileChanged != null)
                        {
                            OnModfileChanged(updatedMod.id, updatedMod.modfile);
                        }
                    };

                    APIClient.GetMod(mod.id, onGetMod, APIClient.LogError);

                    manifest.unresolvedEvents.Remove(modEvent);
                }
            };


            // - Handle ModInfo Event -
            foreach(ModEvent modEvent in eventArray)
            {
                string eventSummary = "TimeStamp (Local)=" + modEvent.dateAdded.AsLocalDateTime();
                eventSummary += "\nMod=" + modEvent.modId;
                eventSummary += "\nEventType=" + modEvent.eventType.ToString();
                
                Debug.Log("[PROCESSING MOD EVENT]\n" + eventSummary);


                manifest.unresolvedEvents.Add(modEvent);

                switch(modEvent.eventType)
                {
                    case ModEvent.EventType.ModfileChanged:
                    {
                        processModfileChange(modEvent);
                    }
                    break;
                    case ModEvent.EventType.ModAvailable:
                    {
                        processModAvailable(modEvent);
                    }
                    break;
                    case ModEvent.EventType.ModUnavailable:
                    {
                        processModUnavailable(modEvent);
                    }
                    break;
                    case ModEvent.EventType.ModEdited:
                    {
                        processModEdited(modEvent);
                    }
                    break;
                    default:
                    {
                        Debug.LogError("Unhandled Event Type: " + modEvent.eventType.ToString());
                    }
                    break;
                }
            }
        }

        private static void UpdateSubscriptions(ModInfo[] subscribedMods)
        {
            if(userData == null) { return; }

            List<int> addedMods = new List<int>();
            List<int> removedMods = new List<int>(userData.subscribedModIDs);
            userData.subscribedModIDs = new List<int>(subscribedMods.Length);

            foreach(ModInfo mod in subscribedMods)
            {
                userData.subscribedModIDs.Add(mod.id);

                if(removedMods.Contains(mod.id))
                {
                    removedMods.Remove(mod.id);
                }
                else
                {
                    addedMods.Add(mod.id);
                }
            }

            WriteUserDataToDisk();

            // - Notify -
            if(OnModSubscriptionAdded != null)
            {
                foreach(int modId in addedMods)
                {
                    OnModSubscriptionAdded(modId);
                }
            }
            if(OnModSubscriptionRemoved != null)
            {
                foreach(int modId in removedMods)
                {
                    OnModSubscriptionRemoved(modId);
                }
            }
        }

        // ---------[ USER MANAGEMENT ]---------
        public static event Action OnUserLoggedOut;
        public static event ModIDEventHandler OnModSubscriptionAdded;
        public static event ModIDEventHandler OnModSubscriptionRemoved;

        public static void RequestSecurityCode(string emailAddress,
                                               Action<APIMessage> onSuccess,
                                               Action<ErrorInfo> onError)
        {
            APIClient.RequestSecurityCode(emailAddress, onSuccess, onError);
        }

        public static void RequestOAuthToken(string securityCode,
                                             Action<string> onSuccess,
                                             Action<ErrorInfo> onError)
        {
            APIClient.RequestOAuthToken(securityCode, onSuccess, onError);
        }

        public static void TryLogUserIn(string userOAuthToken,
                                        Action<User> onSuccess,
                                        Action<ErrorInfo> onError)
        {
            Action<User> onGetUser = (user) =>
            {
                userData = new UserData();
                userData.oAuthToken = userOAuthToken;
                userData.user = user;
                WriteUserDataToDisk();

                onSuccess(user);

                APIClient.GetUserSubscriptions(userOAuthToken,
                                               GetUserSubscriptionsFilter.None,
                                               UpdateSubscriptions,
                                               APIClient.LogError);
            };

            APIClient.GetAuthenticatedUser(userOAuthToken,
                                           onGetUser,
                                           onError);
        }

        public static void LogUserOut()
        {
            userData = null;
            DeleteUserDataFromDisk();
            if(OnUserLoggedOut != null)
            {
                OnUserLoggedOut();
            }
        }

        public static User GetActiveUser()
        {
            return (userData == null ? null : userData.user);
        }

        public static void SubscribeToMod(int modId,
                                          Action<ModInfo> onSuccess,
                                          Action<ErrorInfo> onError)
        {
            APIClient.SubscribeToMod(userData.oAuthToken,
                                     modId,
                                     (message) =>
                                     {
                                        userData.subscribedModIDs.Add(modId);
                                        onSuccess(GetMod(modId));
                                     },
                                     onError);
        }

        public static void UnsubscribeFromMod(int modId,
                                              Action<ModInfo> onSuccess,
                                              Action<ErrorInfo> onError)
        {
            APIClient.UnsubscribeFromMod(userData.oAuthToken,
                                         modId,
                                         (message) =>
                                         {
                                            userData.subscribedModIDs.Remove(modId);
                                            onSuccess(GetMod(modId));
                                         },
                                         onError);
        }

        public static bool IsSubscribedToMod(int modId)
        {
            foreach(int subscribedModID in userData.subscribedModIDs)
            {
                if(subscribedModID == modId) { return true; }
            }
            return false;
        }

        // ---------[ MOD MANAGEMENT ]---------
        public static event ModEventHandler OnModAdded;
        public static event ModIDEventHandler OnModRemoved;
        public static event ModIDEventHandler OnModUpdated;
        public static event ModfileEventHandler OnModfileChanged;
        public static event ModLogoEventHandler OnModLogoUpdated;

        private static Dictionary<int, ModInfo> modCache = new Dictionary<int, ModInfo>();

        public static string GetModDirectory(int modId)
        {
            return MODIO_DIR + "mods/" + modId + "/";
        }

        public static ModInfo GetMod(int modId)
        {
            ModInfo mod = null;
            modCache.TryGetValue(modId, out mod);
            return mod;
        }

        private static void CacheMod(ModInfo mod)
        {
            modCache[mod.id] = mod;
            WriteModToDisk(mod);
        }
        private static void CacheMods(ModInfo[] modArray)
        {
            foreach(ModInfo mod in modArray)
            {
                CacheMod(mod);
            }
        }
        private static void UncacheMod(int modId)
        {
            string modDir = GetModDirectory(modId);
            Directory.Delete(modDir, true);
        }

        public static ModInfo[] GetMods(GetAllModsFilter filter)
        {
            ModInfo[] retVal = filter.FilterCollection(modCache.Values);
            return retVal;
        }

        public static FileDownload StartBinaryDownload(int modId, int modfileId)
        {
            string fileURL = GetModDirectory(modId) + "modfile_" + modfileId + ".zip";
            
            FileDownload download = new FileDownload();
            download.OnCompleted += (d) =>
            {
                // Remove any other binaries
                string[] binaryFilePaths = Directory.GetFiles(GetModDirectory(modId), "modfile_*.zip");
                foreach(string binaryFilePath in binaryFilePaths)
                {
                    if(binaryFilePath != fileURL)
                    {
                        File.Delete(binaryFilePath);
                    }
                }
            };

            Action<Modfile> queueBinaryDownload = (modfile) =>
            {
                download.sourceURL = modfile.download.binaryURL;
                download.fileURL = fileURL;
                download.EnableFilehashVerification(modfile.filehash.md5);

                DownloadManager.AddQueuedDownload(download);
            };

            APIClient.GetModfile(modId, modfileId,
                                 queueBinaryDownload,
                                 download.MarkAsFailed);

            return download;
        }

        public static void DeleteAllDownloadedBinaries(ModInfo mod)
        {
            string[] binaryFilePaths = Directory.GetFiles(GetModDirectory(mod.id), "modfile_*.zip");
            foreach(string binaryFilePath in binaryFilePaths)
            {
                File.Delete(binaryFilePath);
            }
        }

        public static ModBinaryStatus GetBinaryStatus(ModInfo mod)
        {
            if(File.Exists(GetModDirectory(mod.id) + "modfile_" + mod.modfile.id + ".zip"))
            {
                return ModBinaryStatus.UpToDate;
            }
            else
            {
                string[] modfileURLs = Directory.GetFiles(GetModDirectory(mod.id), "modfile_*.zip");
                if(modfileURLs.Length > 0)
                {
                    return ModBinaryStatus.RequiresUpdate;
                }
                else
                {
                    return ModBinaryStatus.Missing;
                }
            }
        }

        public static string GetBinaryPath(ModInfo mod)
        {
            if(File.Exists(GetModDirectory(mod.id) + "modfile_" + mod.modfile.id + ".zip"))
            {
                return GetModDirectory(mod.id) + "modfile_" + mod.modfile.id + ".zip";
            }
            else
            {
                string[] modfileURLs = Directory.GetFiles(GetModDirectory(mod.id), "modfile_*.zip");
                if(modfileURLs.Length > 0)
                {
                    return modfileURLs[0];
                }
            }
            return null;
        }

        // ---------[ LOGO MANAGEMENT ]---------
        private static Dictionary<LogoVersion, string> logoFilenameMap = new Dictionary<LogoVersion, string>()
        {
            { LogoVersion.Original,         "logo_original.png" },
            { LogoVersion.Thumb_320x180,    "logo_320x180.png" },
            { LogoVersion.Thumb_640x360,    "logo_640x360.png" },
            { LogoVersion.Thumb_1280x720,   "logo_1280x720.png" },
        };
        private static Dictionary<LogoVersion, Func<LogoURLInfo, string>> logoServerURLGetterMap = new Dictionary<LogoVersion, Func<LogoURLInfo, string>>()
        {
            { LogoVersion.Original,         (LogoURLInfo logoInfo) => { return logoInfo.original; } },
            { LogoVersion.Thumb_320x180,    (LogoURLInfo logoInfo) => { return logoInfo.thumb320x180; } },
            { LogoVersion.Thumb_640x360,    (LogoURLInfo logoInfo) => { return logoInfo.thumb640x360; } },
            { LogoVersion.Thumb_1280x720,   (LogoURLInfo logoInfo) => { return logoInfo.thumb1280x720; } },
        };

        public static LogoVersion cachedLogoVersion = LogoVersion.Thumb_1280x720;
        public static Dictionary<int, Texture2D> modLogoCache = new Dictionary<int, Texture2D>();

        public static Texture2D LoadCachedModLogo(int modId, LogoVersion logoVersion)
        {
            Texture2D logoTexture = null;

            // NOTE(@jackson): Potentially return an off-res version?
            if(cachedLogoVersion == logoVersion)
            {
               modLogoCache.TryGetValue(modId, out logoTexture);
            }
            #if !TEST_IGNORE_DISK_CACHE
            if(logoTexture == null)
            {
                string localURL = GetModDirectory(modId) + logoFilenameMap[logoVersion];
                if(File.Exists(localURL))
                {
                    logoTexture = new Texture2D(0, 0);
                    logoTexture.LoadImage(File.ReadAllBytes(localURL));
                }
            }
            #endif

            return logoTexture;
        }

        public static void DownloadModLogo(int modId, LogoVersion logoVersion)
        {
            ModInfo modInfo = GetMod(modId);
            if(modInfo != null)
            {
                StartLogoDownload(modInfo, logoVersion);
            }
            else
            {
                Action<ModInfo> cacheModAndDownloadLogo = (mod) =>
                {
                    CacheMod(mod);
                    StartLogoDownload(mod, logoVersion);

                    if(OnModAdded != null)
                    {
                        OnModAdded(mod);
                    }
                };

                APIClient.GetMod(modId,
                                 cacheModAndDownloadLogo,
                                 APIClient.IgnoreResponse);
            }
        }

        private static void StartLogoDownload(ModInfo mod, LogoVersion logoVersion)
        {
            string logoURL = logoServerURLGetterMap[logoVersion](mod.logo);

            TextureDownload download = new TextureDownload();
            download.sourceURL = logoURL;
            download.OnCompleted += (d) =>
            {
                TextureDownload textureDownload = download as TextureDownload;
                Texture2D logoTexture = textureDownload.texture;

                // - Cache -
                if(cachedLogoVersion == logoVersion)
                {
                    modLogoCache[mod.id] = logoTexture;
                }

                // - Save to disk -
                string localURL = GetModDirectory(mod.id) + logoFilenameMap[logoVersion];
                byte[] bytes = logoTexture.EncodeToPNG();
                File.WriteAllBytes(localURL, bytes);

                // - Notify -
                if(OnModLogoUpdated != null)
                {
                    OnModLogoUpdated(mod.id, logoTexture, logoVersion);
                }
            };

            DownloadManager.AddConcurrentDownload(download);
        }

        public static void DownloadModLogos(ModInfo[] modLogosToCache,
                                            LogoVersion logoVersion)
        {
            List<ModInfo> missingLogoList = new List<ModInfo>(modLogosToCache.Length);
            string logoFilename = logoFilenameMap[logoVersion];
            
            // Reset Cache if logoVersion is incorrect
            if(logoVersion != cachedLogoVersion)
            {
                modLogoCache = new Dictionary<int, Texture2D>(modLogosToCache.Length);
            }

            // Check which logos are missing
            foreach(ModInfo mod in modLogosToCache)
            {
                if(modLogoCache.ContainsKey(mod.id))
                {
                    continue;
                }

                #if !TEST_IGNORE_DISK_CACHE
                string logoFilepath = GetModDirectory(mod.id) + logoFilename;
                if(File.Exists(logoFilepath))
                {
                    Texture2D logoTexture = new Texture2D(0,0);
                    logoTexture.LoadImage(File.ReadAllBytes(logoFilepath));

                    modLogoCache[mod.id] = logoTexture;

                    if(OnModLogoUpdated != null)
                    {
                        OnModLogoUpdated(mod.id, logoTexture, logoVersion);
                    }
                }
                else
                #endif
                {
                    missingLogoList.Add(mod);
                }
            }

            // Download
            foreach(ModInfo mod in missingLogoList)
            {
                StartLogoDownload(mod, logoVersion);
            }
        }

        // ---------[ MISC ]------------
        public static void RequestTagCategoryMap(Action<GameTagOption[]> onSuccess,
                                                 Action<ErrorInfo> onError)
        {
            APIClient.GetAllGameTagOptions(onSuccess, onError);
        }

        private static void WriteManifestToDisk()
        {
            File.WriteAllText(MANIFEST_URL, JsonUtility.ToJson(manifest));
        }

        private static void WriteUserDataToDisk()
        {
            File.WriteAllText(USERDATA_URL, JsonUtility.ToJson(userData));
        }

        private static void WriteModToDisk(ModInfo mod)
        {
            string modDir = GetModDirectory(mod.id);
            Directory.CreateDirectory(modDir);
            File.WriteAllText(modDir + "mod.data", JsonUtility.ToJson(mod));
        }

        private static void DeleteUserDataFromDisk()
        {
            File.Delete(USERDATA_URL);
        }

        public static void SubmitModInfo(EditableModInfo modInfo,
                                         Action<ModInfo> modSubmissionSucceeded,
                                         Action<ErrorInfo> modSubmissionFailed)
        {
            // - Edit Mod -
            if(modInfo.id > 0)
            {
                List<Action> submissionActions = new List<Action>();
                int nextActionIndex = 0;
                Action<APIMessage> doNextSubmissionAction = (m) =>
                {
                    if(nextActionIndex < submissionActions.Count)
                    {
                        submissionActions[nextActionIndex++]();
                    }
                };

                if(modInfo.isTagsDirty())
                {
                    submissionActions.Add(() =>
                    {
                        Debug.Log("Submitting Mod Tags");

                        APIClient.AddModTags(userData.oAuthToken,
                                             modInfo.id, modInfo.GetAddedTags(),
                                             doNextSubmissionAction, modSubmissionFailed);
                    });
                }

                if(modInfo.isInfoDirty())
                {
                    submissionActions.Add(() =>
                    {
                        Debug.Log("Submitting Mod Info");

                        APIClient.EditMod(userData.oAuthToken,
                                          modInfo,
                                          modSubmissionSucceeded, modSubmissionFailed);
                    });
                }
                // - Get updated ModInfo if other submissions occurred -
                else if(submissionActions.Count > 0)
                {
                    submissionActions.Add(() =>
                    {
                        APIClient.GetMod(modInfo.id,
                                         modSubmissionSucceeded, modSubmissionFailed);
                    });
                }
                // - Just notify succeeded -
                else
                {
                    submissionActions.Add(() =>
                    {
                        modSubmissionSucceeded(modInfo);
                    });
                }

                // - Start submission chain -
                doNextSubmissionAction(null);
            }
            // - Add Mod -
            else
            {
                APIClient.AddMod(userData.oAuthToken,
                                 modInfo,
                                 modSubmissionSucceeded, modSubmissionFailed);
            }
        }

        public static void AddModMedia(ModMediaChanges modMedia,
                                       Action<APIMessage> onSuccess,
                                       Action<ErrorInfo> onError)
        {
            Debug.Assert(modMedia.modId > 0,
                         "Invalid mod id supplied with the mod media changes");

            // - Image Gallery -
            BinaryUpload imageGalleryUpload = null;
            if(modMedia.images.Length > 0)
            {
                string galleryZipLocation = Application.temporaryCachePath + "/modio/imageUpload_" + DateTime.Now.ToFileTime() + ".zip";
                ZipUtil.Zip(galleryZipLocation, modMedia.images);

                imageGalleryUpload = new BinaryUpload()
                {
                    fileName = "images.zip",
                    data = File.ReadAllBytes(galleryZipLocation)
                };
            }

            APIClient.AddModMedia(userData.oAuthToken, modMedia.modId,
                                  null, imageGalleryUpload,
                                  modMedia.youtube, modMedia.sketchfab,
                                  onSuccess, onError);
        }

        public static void DeleteModMedia(ModMediaChanges modMedia,
                                          Action<APIMessage> onSuccess,
                                          Action<ErrorInfo> onError)
        {
            Debug.Assert(modMedia.modId > 0,
                         "Invalid mod id supplied with the mod media changes");

            APIClient.DeleteModMedia(userData.oAuthToken,
                                     modMedia,
                                     onSuccess, onError);
        }

        public static void UploadModBinary_Unzipped(string unzippedBinaryLocation,
                                                    ModfileProfile profile,
                                                    bool setPrimary,
                                                    Action<Modfile> onSuccess,
                                                    Action<ErrorInfo> onError)
        {
            string binaryZipLocation = Application.temporaryCachePath + "/modio/" + System.IO.Path.GetFileNameWithoutExtension(unzippedBinaryLocation) + DateTime.Now.ToFileTime() + ".zip";

            ZipUtil.Zip(binaryZipLocation, unzippedBinaryLocation);

            UploadModBinary_Zipped(binaryZipLocation, profile, setPrimary, onSuccess, onError);
        }

        public static void UploadModBinary_Zipped(string binaryZipLocation,
                                                  ModfileProfile profile,
                                                  bool setPrimary,
                                                  Action<Modfile> onSuccess,
                                                  Action<ErrorInfo> onError)
        {
            string buildFilename = Path.GetFileName(binaryZipLocation);
            byte[] buildZipData = File.ReadAllBytes(binaryZipLocation);

            APIClient.AddModfile(userData.oAuthToken,
                                 profile,
                                 buildFilename, buildZipData,
                                 setPrimary,
                                 onSuccess, onError);
        }

        // --- TEMPORARY PASS-THROUGH FUNCTIONS ---
        public static void AddGameMedia(UnsubmittedGameMedia gameMedia,
                                        Action<APIMessage> onSuccess,
                                        Action<ErrorInfo> onError)
        {
            APIClient.AddGameMedia(userData.oAuthToken,
                                   gameMedia, onSuccess, onError);
        }

        public static void AddGameTagOption(UnsubmittedGameTagOption tagOption,
                                            Action<APIMessage> onSuccess,
                                            Action<ErrorInfo> onError)
        {
            APIClient.AddGameTagOption(userData.oAuthToken,
                                       tagOption, onSuccess, onError);
        }

        public static void AddPositiveRating(int modId,
                                             Action<APIMessage> onSuccess,
                                             Action<ErrorInfo> onError)
        {
            APIClient.AddModRating(userData.oAuthToken,
                                   modId, 1, onSuccess, onError);
        }

        public static void AddModKVPMetadata(int modId, UnsubmittedMetadataKVP[] metadataKVPs,
                                             Action<APIMessage> onSuccess,
                                             Action<ErrorInfo> onError)
        {
            APIClient.AddModKVPMetadata(userData.oAuthToken,
                                        modId, metadataKVPs,
                                        onSuccess, onError);
        }

        public static void AddModTeamMember(int modId, UnsubmittedTeamMember teamMember,
                                            Action<APIMessage> onSuccess,
                                            Action<ErrorInfo> onError)
        {
            APIClient.AddModTeamMember(userData.oAuthToken,
                                       modId, teamMember,
                                       onSuccess, onError);
        }

        public static void DeleteMod(int modId,
                                     Action<APIMessage> onSuccess,
                                     Action<ErrorInfo> onError)
        {
            APIClient.DeleteMod(userData.oAuthToken,
                                modId,
                                onSuccess, onError);
        }

        public static void DeleteModTags(int modId, string[] tagsToDelete,
                                         Action<APIMessage> onSuccess,
                                         Action<ErrorInfo> onError)
        {
            APIClient.DeleteModTags(userData.oAuthToken,
                                    modId, tagsToDelete,
                                    onSuccess, onError);
        }

        public static void DeleteModDependencies(int modId, int[] modIdsToRemove,
                                                 Action<APIMessage> onSuccess,
                                                 Action<ErrorInfo> onError)
        {
            APIClient.DeleteModDependencies(userData.oAuthToken,
                                            modId, modIdsToRemove,
                                            onSuccess, onError);
        }

        public static void AddModDependencies(int modId, int[] modIdsToRemove,
                                              Action<APIMessage> onSuccess,
                                              Action<ErrorInfo> onError)
        {
            APIClient.AddModDependencies(userData.oAuthToken,
                                         modId, modIdsToRemove,
                                         onSuccess, onError);
        }

        public static void DeleteModKVPMetadata(int modId, UnsubmittedMetadataKVP[] metadataKVPs,
                                                Action<APIMessage> onSuccess,
                                                Action<ErrorInfo> onError)
        {
            APIClient.DeleteModKVPMetadata(userData.oAuthToken,
                                           modId, metadataKVPs,
                                           onSuccess, onError);
        }

        public static void DeleteModTeamMember(int modId, int teamMemberId,
                                               Action<APIMessage> onSuccess,
                                               Action<ErrorInfo> onError)
        {
            APIClient.DeleteModTeamMember(userData.oAuthToken,
                                          modId, teamMemberId,
                                          onSuccess, onError);
        }

        public static void DeleteModComment(int modId, int commentId,
                                            Action<APIMessage> onSuccess,
                                            Action<ErrorInfo> onError)
        {
            APIClient.DeleteModComment(userData.oAuthToken,
                                       modId, commentId,
                                       onSuccess, onError);
        }

        public static void GetAllModTeamMembers(int modId,
                                                Action<TeamMember[]> onSuccess,
                                                Action<ErrorInfo> onError)
        {
            APIClient.GetAllModTeamMembers(modId, GetAllModTeamMembersFilter.None,
                                           onSuccess, onError);
        }

        public static void DeleteGameTagOption(GameTagOptionToDelete gameTagOption,
                                               Action<APIMessage> onSuccess,
                                               Action<ErrorInfo> onError)
        {
            APIClient.DeleteGameTagOption(userData.oAuthToken,
                                          gameTagOption,
                                          onSuccess, onError);
        }
    }
}