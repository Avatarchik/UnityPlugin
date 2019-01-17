﻿#if UNITY_EDITOR
namespace ModIO.InEditor
{
    public static class EditorSettings
    {
        public static readonly ServerSettings TEST_SERVER = new ServerSettings()
        {
            apiURL = APIClient.API_URL_TESTSERVER + APIClient.API_VERSION,
            cacheDir = System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "modio_test"),
            gameId = 0,
            gameAPIKey = string.Empty,
        };
        public static readonly ServerSettings PRODUCTION_SERVER = new ServerSettings()
        {
            apiURL = APIClient.API_URL_PRODUCTIONSERVER + APIClient.API_VERSION,
            cacheDir = System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "modio"),
            gameId = 0,
            gameAPIKey = string.Empty,
        };

        public const bool USE_TEST_SERVER = true;
        public const bool DEBUG_ALL_REQUESTS = false;

        public static void Load()
        {
            #pragma warning disable 0162
            ServerSettings settings;
            if(USE_TEST_SERVER)
            {
                settings = TEST_SERVER;

            }
            else
            {
                settings = PRODUCTION_SERVER;
            }
            #pragma warning restore 0162

            APIClient.apiURL = settings.apiURL;
            APIClient.gameId = settings.gameId;
            APIClient.gameAPIKey = settings.gameAPIKey;
            APIClient.logAllRequests = DEBUG_ALL_REQUESTS;

            DownloadClient.logAllRequests = DEBUG_ALL_REQUESTS;

            var cacheSettings = CacheClient.settings;
            cacheSettings.directory = settings.cacheDir;
            CacheClient.settings = cacheSettings;
        }
    }
}
#endif