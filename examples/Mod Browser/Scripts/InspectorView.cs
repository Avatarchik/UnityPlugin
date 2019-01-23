﻿using System;
using System.Collections.Generic;
using System.Text;

using UnityEngine;
using UnityEngine.UI;

namespace ModIO.UI
{
    public class InspectorView : MonoBehaviour
    {
        // ---------[ FIELDS ]---------
        public event Action<ModProfile> subscribeRequested;
        public event Action<ModProfile> unsubscribeRequested;
        public event Action<ModProfile> enableRequested;
        public event Action<ModProfile> disableRequested;

        [Header("Settings")]
        public GameObject versionHistoryItemPrefab;
        public string missingVersionChangelogText;

        [Header("UI Components")]
        public ModView modView;
        public ModMediaDisplayComponent selectedMediaPreview;
        public RectTransform versionHistoryContainer;
        public ScrollRect scrollView;
        public Button previousModButton;
        public Button nextModButton;
        public Button backToDiscoverButton;
        public Button backToSubscriptionsButton;

        // ---[ RUNTIME DATA ]---
        [Header("Runtime Data")]
        public ModProfile profile;
        public ModStatistics statistics;
        public bool isModSubscribed;
        public bool isModEnabled;

        // ---[ TEMP DATA ]---
        [Header("Temp Data")]
        public float mediaElementHeight;
        public IEnumerable<ModTagCategory> tagCategories { get; set; }

        // ---------[ INITIALIZATION ]---------
        public void Initialize()
        {
            // TODO(@jackson): Asserts
            if(modView != null)
            {
                modView.Initialize();
            }

            if(selectedMediaPreview != null)
            {
                selectedMediaPreview.Initialize();
                selectedMediaPreview.onClick += (d) =>
                {
                    if(d.data.mediaType == ImageDisplayData.MediaType.YouTubeThumbnail)
                    {
                        UIUtilities.OpenYouTubeVideoURL(d.data.youTubeId);
                    }
                };

                ModMediaContainer container = modView.mediaContainer as ModMediaContainer;
                if(container != null)
                {
                    container.logoClicked += MediaPreview_Logo;
                    container.galleryImageClicked += MediaPreview_GalleryImage;
                    container.youTubeThumbnailClicked += MediaPreview_YouTubeThumbnail;
                }
            }

            if(modView.statisticsDisplay != null)
            {
                modView.statisticsDisplay.Initialize();
            }

            if((versionHistoryContainer != null && versionHistoryItemPrefab == null)
               || (versionHistoryItemPrefab != null && versionHistoryContainer == null))
            {
                Debug.LogWarning("[mod.io] In order to display a version history both the "
                                 + "versionHistoryItemPrefab and versionHistoryContainer variables must "
                                 + "be set for the InspectorView.", this);
            }

            Debug.Assert(!(versionHistoryItemPrefab != null && versionHistoryItemPrefab.GetComponent<ModfileDisplayComponent>() == null),
                         "[mod.io] The versionHistoryItemPrefab requires a ModfileDisplayComponent on the root Game Object.");
        }

        // ---------[ UPDATE VIEW ]---------
        [Obsolete]
        public void UpdateProfileDisplay()
        {
            DisplayMod(profile, statistics, tagCategories, isModSubscribed, isModEnabled);
        }

        [Obsolete]
        public void UpdateStatisticsDisplay()
        {
            DisplayMod(profile, statistics, tagCategories, isModSubscribed, isModEnabled);
        }

        public void DisplayMod(ModProfile profile, ModStatistics statistics,
                               IEnumerable<ModTagCategory> tagCategories,
                               bool isModSubscribed, bool isModEnabled)
        {
            Debug.Assert(profile != null);

            if(modView != null)
            {
                modView.DisplayMod(profile, statistics,
                                   tagCategories,
                                   isModSubscribed, isModEnabled);
            }

            if(selectedMediaPreview != null)
            {
                selectedMediaPreview.DisplayLogo(profile.id, profile.logoLocator);
            }

            if(modView.mediaContainer != null)
            {
                ModMediaCollection media = profile.media;
                bool hasMedia = media != null;
                hasMedia &= ((media.youTubeURLs != null && media.youTubeURLs.Length > 0)
                             || (media.galleryImageLocators != null && media.galleryImageLocators.Length > 0));

                modView.mediaContainer.gameObject.SetActive(hasMedia);
            }

            // - version history -
            if(versionHistoryContainer != null
               && versionHistoryItemPrefab != null)
            {
                foreach(Transform t in versionHistoryContainer)
                {
                    GameObject.Destroy(t.gameObject);
                }

                RequestFilter modfileFilter = new RequestFilter();
                modfileFilter.sortFieldName = ModIO.API.GetAllModfilesFilterFields.dateAdded;
                modfileFilter.isSortAscending = false;

                // TODO(@jackson): onError - queue
                APIClient.GetAllModfiles(profile.id,
                                         modfileFilter,
                                         new APIPaginationParameters(){ limit = 20 },
                                         (r) => PopulateVersionHistory(profile.id, r.items),
                                         WebRequestError.LogAsWarning);
            }
        }

        public void DisplayLoading()
        {
            modView.DisplayLoading();
            selectedMediaPreview.DisplayLoading();
        }

        // ---------[ UI ELEMENT CREATION ]---------
        private void PopulateVersionHistory(int modId, IEnumerable<Modfile> modfiles)
        {
            #if UNITY_EDITOR
            if(!Application.isPlaying) { return; }
            #endif

            // inspector has closed/changed mods since call was made
            if(profile.id != modId) { return; }

            foreach(Modfile modfile in modfiles)
            {
                GameObject go = GameObject.Instantiate(versionHistoryItemPrefab, versionHistoryContainer) as GameObject;
                go.name = "Mod Version: " + modfile.version;

                if(String.IsNullOrEmpty(modfile.changelog))
                {
                    modfile.changelog = missingVersionChangelogText;
                }

                var entry = go.GetComponent<ModfileDisplayComponent>();
                entry.Initialize();
                entry.DisplayModfile(modfile);
            }
        }

        // ---------[ EVENTS ]---------
        public void NotifySubscribeRequested()
        {
            if(subscribeRequested != null)
            {
                subscribeRequested(this.profile);
            }
        }
        public void NotifyUnsubscribeRequested()
        {
            if(unsubscribeRequested != null)
            {
                unsubscribeRequested(this.profile);
            }
        }
        public void NotifyEnableRequested()
        {
            if(enableRequested != null)
            {
                enableRequested(this.profile);
            }
        }
        public void NotifyDisableRequested()
        {
            if(disableRequested != null)
            {
                disableRequested(this.profile);
            }
        }

        private void MediaPreview_Logo(ModLogoDisplayComponent display)
        {
            ImageDisplayData imageData = display.data;
            selectedMediaPreview.data = imageData;

            if(display.logoSize != selectedMediaPreview.logoSize)
            {
                ModManager.GetModLogo(profile, selectedMediaPreview.logoSize,
                                      (t) =>
                                      {
                                        if(Application.isPlaying
                                           && selectedMediaPreview.data.Equals(imageData))
                                        {
                                            imageData.texture = t;
                                            selectedMediaPreview.data = imageData;
                                        }
                                      },
                                      WebRequestError.LogAsWarning);
            }
        }
        private void MediaPreview_GalleryImage(ModGalleryImageDisplayComponent display)
        {
            ImageDisplayData imageData = display.data;
            selectedMediaPreview.data = imageData;

            if(display.imageSize != selectedMediaPreview.galleryImageSize)
            {
                ModManager.GetModGalleryImage(profile, display.data.fileName,
                                              selectedMediaPreview.galleryImageSize,
                                              (t) =>
                                              {
                                                if(Application.isPlaying
                                                   && selectedMediaPreview.data.Equals(imageData))
                                                {
                                                    imageData.texture = t;
                                                    selectedMediaPreview.data = imageData;
                                                }
                                              },
                                              WebRequestError.LogAsWarning);
            }
        }
        private void MediaPreview_YouTubeThumbnail(YouTubeThumbnailDisplayComponent display)
        {
            ImageDisplayData displayData = display.data;
            selectedMediaPreview.data = displayData;
        }
    }
}
