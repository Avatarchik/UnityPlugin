﻿using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

namespace ModIO.UI
{
    public enum PageTransitionDirection
    {
        FromLeft,
        FromRight,
    }

    public class ExplorerView : MonoBehaviour
    {
        // ---------[ NESTED DATA-TYPES ]---------
        /// <summary>Event for notifying listeners of a change to displayed mods.</summary>
        [Serializable]
        public class ModPageChanged : UnityEngine.Events.UnityEvent<RequestPage<ModProfile>> {}
        /// <summary>Event for notifying listeners of a change to the request filter.</summary>
        [Serializable]
        public class RequestFilterChanged : UnityEngine.Events.UnityEvent<RequestFilter> {}

        /// <summary>Sort method data.</summary>
        [Serializable]
        public struct SortMethod
        {
            public bool ascending;
            public string fieldName;
        }

        // ---------[ FIELDS ]---------
        [Header("UI Components")]
        /// <summary>Container used to display mods.</summary>
        public ModContainer containerTemplate = null;

        /// <summary>Button for transitioning to the previous page of results.</summary>
        public Button prevPageButton;

        /// <summary>Button for transitioning to the next page of results.</summary>
        public Button nextPageButton;

        /// <summary>Object to display when no results were found.</summary>
        [Tooltip("Object to display when no results were found.")]
        public GameObject noResultsDisplay;

        /// <summary>Object that should react to this view being enabled/disabled.</summary>
        public StateToggleDisplay isActiveIndicator;

        [Header("Settings")]
        /// <summary>Default sort method.</summary>
        public SortMethod defaultSortMethod = new SortMethod()
        {
            ascending = false,
            fieldName = API.GetAllModsFilterFields.dateLive,
        };

        /// <summary>Number of seconds per page transition.</summary>
        public float pageTransitionTimeSeconds = 0.4f;

        [Header("Events")]
        /// <summary>Event for notifying listeners of a change to displayed mods.</summary>
        public ModPageChanged onModPageChanged = null;
        /// <summary>Event for notifying listeners of a change to the request filter.</summary>
        public RequestFilterChanged onRequestFilterChanged = null;


        // --- Run-time Data ---
        /// <summary>RequestPage being displayed.</summary>
        private RequestPage<ModProfile> m_modPage = null;

        /// <summary>RequestPage being transitioned to.</summary>
        private RequestPage<ModProfile> m_transitionPage = null;

        /// <summary>Currently applied RequestFilter.</summary>
        private RequestFilter m_requestFilter = new RequestFilter();

        // --- Accessors ---
        /// <summary>RequestPage being displayed.</summary>
        public RequestPage<ModProfile> modPage
        {
            get { return this.m_modPage; }
        }

        /// <summary>RequestPage being transitioned to.</summary>
        public RequestPage<ModProfile> transitionPage
        {
            get { return this.m_transitionPage; }
        }

        /// <summary>Currently applied RequestFilter.</summary>
        public RequestFilter requestFilter
        {
            get { return this.m_requestFilter; }
        }

        /// <summary>Filter currently applied to the mod name.</summary>
        protected EqualToFilter<string> nameFieldFilter
        {
            get
            {
                List<IRequestFieldFilter> filterList = null;
                if(this.m_requestFilter.fieldFilterMap.TryGetValue(ModIO.API.GetAllModsFilterFields.fullTextSearch, out filterList)
                   && filterList != null
                   && filterList.Count > 0)
                {
                    return filterList[0] as EqualToFilter<string>;
                }

                return null;
            }
            set
            {
                if(value == null)
                {
                    this.m_requestFilter.fieldFilterMap.Remove(ModIO.API.GetAllModsFilterFields.fullTextSearch);
                }
                else
                {
                    List<IRequestFieldFilter> filterList = null;
                    if(this.m_requestFilter.fieldFilterMap.TryGetValue(ModIO.API.GetAllModsFilterFields.fullTextSearch, out filterList)
                       && filterList != null
                       && filterList.Count > 0)
                    {
                        filterList[0] = value;
                    }
                    else
                    {
                        this.m_requestFilter.AddFieldFilter(ModIO.API.GetAllModsFilterFields.fullTextSearch, value);
                    }
                }
            }
        }

        /// <summary>Filter currently applied to the tags field.</summary>
        protected MatchesArrayFilter<string> tagMatchFieldFilter
        {
            get
            {
                List<IRequestFieldFilter> filterList = null;
                if(this.m_requestFilter.fieldFilterMap.TryGetValue(ModIO.API.GetAllModsFilterFields.tags, out filterList)
                   && filterList != null
                   && filterList.Count > 0)
                {
                    return filterList[0] as MatchesArrayFilter<string>;
                }

                return null;
            }
            set
            {
                if(value == null)
                {
                    this.m_requestFilter.fieldFilterMap.Remove(ModIO.API.GetAllModsFilterFields.tags);
                }
                else
                {
                    List<IRequestFieldFilter> filterList = null;
                    if(this.m_requestFilter.fieldFilterMap.TryGetValue(ModIO.API.GetAllModsFilterFields.tags, out filterList)
                       && filterList != null
                       && filterList.Count > 0)
                    {
                        filterList[0] = value;
                    }
                    else
                    {
                        this.m_requestFilter.AddFieldFilter(ModIO.API.GetAllModsFilterFields.tags, value);
                    }
                }
            }
        }

        // ---------[ OLD ]---------
        // --- Run-time Data ---
        private ModContainer m_currentPageContainer = null;
        private ModContainer m_targetPageContainer = null;

        private bool m_isTransitioning = false;

        private RequestPage<ModProfile> m_targetPage = null;
        public RequestPage<ModProfile> targetPage
        {
            get { return this.targetPage; }
        }

        private ModProfileRequestManager profileManager { get { return ModProfileRequestManager.instance; } }

        // ---------[ INITIALIZATION ]---------
        /// <summary>Asserts values and initializes templates.</summary>
        protected virtual void Awake()
        {
            Debug.Assert(this.gameObject != this.containerTemplate.gameObject,
                         "[mod.io] The Explorer View and its Container Template cannot be the same"
                         + " Game Object. Please create a separate Game Object for the container template.");

            // -- initialize template ---
            // NOTE(@jackson): Initializing in Start() was too late as isActiveAndEnabled is true
            // before Start() was called. This may mean that other calls are being made too early,
            // specifically the OnGameProfileUpdated() in ModBrowser.
            this.containerTemplate.gameObject.SetActive(false);

            GameObject templateCopyGO;

            // current page
            templateCopyGO = GameObject.Instantiate(this.containerTemplate.gameObject,
                                                    this.containerTemplate.transform.parent);
            templateCopyGO.name = "Mod Page A";
            // TODO(@jackson): Change this...
            templateCopyGO.SetActive(true);
            templateCopyGO.transform.SetSiblingIndex(this.containerTemplate.transform.GetSiblingIndex() + 1);
            this.m_currentPageContainer = templateCopyGO.GetComponent<ModContainer>();

            // transition page
            templateCopyGO = GameObject.Instantiate(this.containerTemplate.gameObject,
                                                    this.containerTemplate.transform.parent);
            templateCopyGO.name = "Mod Page B";
            templateCopyGO.SetActive(false);
            templateCopyGO.transform.SetSiblingIndex(this.containerTemplate.transform.GetSiblingIndex() + 2);
            this.m_targetPageContainer = templateCopyGO.GetComponent<ModContainer>();
        }

        /// <summary>Collects view elements.</summary>
        protected virtual void Start()
        {
            #if UNITY_EDITOR
            ExplorerView[] nested = this.gameObject.GetComponentsInChildren<ExplorerView>(true);
            if(nested.Length > 1)
            {
                ExplorerView nestedView = nested[1];
                if(nestedView == this)
                {
                    nestedView = nested[0];
                }

                Debug.LogError("[mod.io] Nesting ExplorerViews is currently not supported due to the"
                               + " way IExplorerViewElement component parenting works."
                               + "\nThe nested ExplorerViews must be removed to allow ExplorerView functionality."
                               + "\nthis=" + this.gameObject.name
                               + "\nnested=" + nestedView.gameObject.name,
                               this);
                return;
            }
            #endif

            // assign view elements to this
            var viewElementChildren = this.gameObject.GetComponentsInChildren<IExplorerViewElement>(true);
            foreach(IExplorerViewElement viewElement in viewElementChildren)
            {
                viewElement.SetExplorerView(this);
            }

            // - create pages -
            this.UpdateCurrentPageDisplay();
            this.UpdatePageButtonInteractibility();

            // - perform initial fetch -
            this.Refresh();
        }

        // TODO(@jackson): Recheck page size
        private void OnEnable()
        {
            // NOTE(@jackson): This appears to be unnecessary?
            // UpdateCurrentPageDisplay();

            if(this.isActiveIndicator != null)
            {
                this.isActiveIndicator.isOn = true;
            }
        }

        private void OnDisable()
        {
            if(this.isActiveIndicator != null)
            {
                this.isActiveIndicator.isOn = false;
            }
        }

        public void Refresh()
        {
            int pageIndex = 0;
            int pageSize = this.m_currentPageContainer.itemLimit;
            if(pageSize < 0)
            {
                pageSize = APIPaginationParameters.LIMIT_MAX;
            }
            int pageOffset = pageIndex * pageSize;
            bool wasDisplayUpdated = false;

            RequestPage<ModProfile> filteredPage = new RequestPage<ModProfile>()
            {
                size = pageSize,
                items = new ModProfile[pageSize],
                resultOffset = pageOffset,
                resultTotal = 0,
            };
            this.m_modPage = filteredPage;

            ModProfileRequestManager.instance.FetchModProfilePage(this.m_requestFilter, pageOffset, pageSize,
            (page) =>
            {
                if(this != null
                   && this.m_modPage == filteredPage)
                {
                    this.DisplayModPage(page);
                    wasDisplayUpdated = true;
                }
            },
            WebRequestError.LogAsWarning);

            if(!wasDisplayUpdated)
            {
                // force updates
                this.m_modPage = null;
                this.DisplayModPage(filteredPage);
            }
        }

        public void UpdatePageButtonInteractibility()
        {
            if(this.prevPageButton != null)
            {
                this.prevPageButton.interactable = (!this.m_isTransitioning
                                                    && this.modPage != null
                                                    && this.modPage.CalculatePageIndex() > 0);
            }
            if(this.nextPageButton != null)
            {
                this.nextPageButton.interactable = (!this.m_isTransitioning
                                                    && this.modPage != null
                                                    && this.modPage.CalculatePageIndex()+1 < this.modPage.CalculatePageCount());
            }
        }

        public void ChangePage(int pageDifferential)
        {
            Debug.Assert(this.modPage != null);

            if(this.m_isTransitioning)
            {
                return;
            }

            int pageSize = this.m_currentPageContainer.itemLimit;
            if(pageSize < 0)
            {
                pageSize = APIPaginationParameters.LIMIT_MAX;
            }

            int targetPageIndex = this.m_modPage.CalculatePageIndex() + pageDifferential;
            int targetPageProfileOffset = targetPageIndex * pageSize;

            Debug.Assert(targetPageIndex >= 0);
            Debug.Assert(targetPageIndex < this.m_modPage.CalculatePageCount());

            int pageItemCount = (int)Mathf.Min(pageSize,
                                               this.m_modPage.resultTotal - targetPageProfileOffset);

            RequestPage<ModProfile> m_targetPage = new RequestPage<ModProfile>()
            {
                size = pageSize,
                items = new ModProfile[pageItemCount],
                resultOffset = targetPageProfileOffset,
                resultTotal = this.m_modPage.resultTotal,
            };
            this.m_targetPage = m_targetPage;
            this.UpdateTargetPageDisplay();

            ModProfileRequestManager.instance.FetchModProfilePage(this.m_requestFilter, targetPageProfileOffset, pageSize,
            (page) =>
            {
                if(this.m_targetPage == m_targetPage)
                {
                    this.m_targetPage = page;
                    this.UpdateTargetPageDisplay();
                }
                if(this.m_modPage == m_targetPage)
                {
                    this.DisplayModPage(page);
                }
            },
            null);

            PageTransitionDirection transitionDirection = (pageDifferential < 0
                                                           ? PageTransitionDirection.FromLeft
                                                           : PageTransitionDirection.FromRight);

            this.InitiateTargetPageTransition(transitionDirection, null);
            this.UpdatePageButtonInteractibility();
        }

        // ---------[ FILTER CONTROL ]---------
        /// <summary>Sets the title filter and refreshes the view.</summary>
        public void SetNameFieldFilter(string nameFilter)
        {
            EqualToFilter<string> oldFilter = this.nameFieldFilter;

            // null-checks
            if(nameFilter == null) { nameFilter = string.Empty; }

            string oldFilterValue = string.Empty;
            if(oldFilter != null
               && oldFilter.filterValue != null)
            {
                oldFilterValue = oldFilter.filterValue;
            }

            // apply filter
            if(oldFilterValue.ToUpper() != nameFilter.ToUpper())
            {
                // set
                if(String.IsNullOrEmpty(nameFilter))
                {
                    this.nameFieldFilter = null;
                }
                else
                {
                    EqualToFilter<string> newFieldFilter = new EqualToFilter<string>()
                    {
                        filterValue = nameFilter,
                    };
                    this.nameFieldFilter = newFieldFilter;
                }

                // refresh
                if(this.isActiveAndEnabled) { this.Refresh(); }

                // notify
                if(this.onRequestFilterChanged != null)
                {
                    this.onRequestFilterChanged.Invoke(this.m_requestFilter);
                }
            }
        }

        /// <summary>Gets the title filter string.</summary>
        public string GetTitleFilter()
        {
            EqualToFilter<string> filter = this.nameFieldFilter;
            if(filter == null)
            {
                return null;
            }
            else
            {
                return filter.filterValue;
            }
        }

        /// <summary>Sets the sort method and refreshes the view.</summary>
        public void SetSortMethod(SortMethod sortMethod)
        {
            // null-checks
            if(sortMethod.fieldName == null)
            {
                sortMethod.fieldName = string.Empty;
            }

            // apply filter
            if(this.m_requestFilter.sortFieldName.ToUpper() != sortMethod.fieldName.ToUpper()
               || this.m_requestFilter.isSortAscending != sortMethod.ascending)
            {
                this.m_requestFilter.sortFieldName = sortMethod.fieldName;
                this.m_requestFilter.isSortAscending = sortMethod.ascending;

                // refresh
                if(this.isActiveAndEnabled) { this.Refresh(); }

                // notify
                if(this.onRequestFilterChanged != null)
                {
                    this.onRequestFilterChanged.Invoke(this.m_requestFilter);
                }
            }
        }

        /// <summary>Sets the sort method and refreshes the view.</summary>
        public void SetSortMethod(bool ascending, string fieldName)
        {
            // create struct
            SortMethod sortMethod = new SortMethod()
            {
                ascending = ascending,
                fieldName = fieldName,
            };

            this.SetSortMethod(sortMethod);
        }

        /// <summary>Gets the sort method.</summary>
        public SortMethod GetSortMethod()
        {
            return new SortMethod()
            {
                ascending = this.m_requestFilter.isSortAscending,
                fieldName = this.m_requestFilter.sortFieldName,
            };
        }

        /// <summary>Sets the tag filter and refreshes the results.</summary>
        public void SetTagFilter(IList<string> tagFilter)
        {
            MatchesArrayFilter<string> oldFilter = this.tagMatchFieldFilter;

            // null-checks
            if(tagFilter == null) { tagFilter = new string[0]; }

            string[] oldFilterValue = new string[0];
            if(oldFilter != null)
            {
                oldFilterValue = oldFilter.filterArray;
            }

            // check if same and copy
            bool isSame = (oldFilterValue.Length == tagFilter.Count);
            string[] newFilterValue = new string[tagFilter.Count];

            if(tagFilter != oldFilterValue)
            {
                for(int i = 0;
                    i < newFilterValue.Length;
                    ++i)
                {
                    newFilterValue[i] = tagFilter[i];

                    isSame = isSame && (oldFilterValue[i] == newFilterValue[i]);
                }
            }

            // apply
            if(!isSame)
            {
                // set
                if(newFilterValue.Length == 0)
                {
                    this.tagMatchFieldFilter = null;
                }
                else
                {
                    MatchesArrayFilter<string> newFilter = new MatchesArrayFilter<string>()
                    {
                        filterArray = newFilterValue,
                    };
                    this.tagMatchFieldFilter = newFilter;
                }

                // refresh
                if(this.isActiveAndEnabled) { this.Refresh(); }

                // notify
                if(this.onRequestFilterChanged != null)
                {
                    this.onRequestFilterChanged.Invoke(this.m_requestFilter);
                }
            }
        }

        /// <summary>Gets the tag filter.</summary>
        public string[] GetTagFilter()
        {
            MatchesArrayFilter<string> filter = this.tagMatchFieldFilter;
            if(filter == null)
            {
                return null;
            }
            else
            {
                return filter.filterArray;
            }
        }

        /// <summary>Adds a tag to the tag filter and refreshes the results.</summary>
        public void AddTagToFilter(string tagName)
        {
            // get existing
            MatchesArrayFilter<string> tagFilter = this.tagMatchFieldFilter;
            if(tagFilter == null)
            {
                tagFilter = new MatchesArrayFilter<string>();
                tagFilter.filterValue = new string[0];
            }

            List<string> tagFilterValues = new List<string>();
            tagFilterValues.AddRange(tagFilter.filterArray);

            // add
            if(!tagFilterValues.Contains(tagName))
            {
                tagFilterValues.Add(tagName);
                tagFilter.filterArray = tagFilterValues.ToArray();

                this.tagMatchFieldFilter = tagFilter;

                // refresh
                if(this.isActiveAndEnabled) { this.Refresh(); }

                // notify
                if(this.onRequestFilterChanged != null)
                {
                    this.onRequestFilterChanged.Invoke(this.m_requestFilter);
                }
            }
        }

        /// <summary>Removes a tag from the tag filter and refreshes the results.</summary>
        public void RemoveTagFromFilter(string tagName)
        {
            MatchesArrayFilter<string> tagFilter = this.tagMatchFieldFilter;

            // early out
            if(tagFilter == null
               || tagFilter.filterArray == null
               || tagFilter.filterArray.Length == 0)
            {
                return;
            }

            // create list
            List<string> tagFilterValues = new List<string>(tagFilter.filterArray);

            if(tagFilterValues.Contains(tagName))
            {
                tagFilterValues.Remove(tagName);

                if(tagFilterValues.Count == 0)
                {
                    this.tagMatchFieldFilter = null;
                }

                // refresh
                if(this.isActiveAndEnabled) { this.Refresh(); }

                // notify
                if(this.onRequestFilterChanged != null)
                {
                    this.onRequestFilterChanged.Invoke(this.m_requestFilter);
                }
            }
        }

        // ---------[ PAGE DISPLAY ]---------
        public void UpdateCurrentPageDisplay()
        {
            if(this.m_currentPageContainer == null) { return; }

            #if DEBUG
            if(m_isTransitioning)
            {
                Debug.LogWarning("[mod.io] Explorer View is currently transitioning between pages. It"
                                 + " is recommended to not update page displays at this time.");
            }
            #endif

            if(noResultsDisplay != null)
            {
                noResultsDisplay.SetActive(this.m_modPage == null
                                           || this.m_modPage.items == null
                                           || this.m_modPage.items.Length == 0);
            }

            IList<ModProfile> profiles = null;
            if(this.m_modPage != null)
            {
                profiles = this.m_modPage.items;
            }

            this.DisplayProfiles(profiles, this.m_currentPageContainer);
        }

        public void UpdateTargetPageDisplay()
        {
            if(this.m_targetPageContainer == null) { return; }

            #if DEBUG
            if(m_isTransitioning)
            {
                Debug.LogWarning("[mod.io] Explorer View is currently transitioning between pages. It"
                                 + " is recommended to not update page displays at this time.");
            }
            #endif

            this.DisplayProfiles(this.m_targetPage.items, this.m_targetPageContainer);
        }

        protected virtual void DisplayProfiles(IList<ModProfile> profileCollection, ModContainer modContainer)
        {
            Debug.Assert(modContainer != null);

            if(profileCollection == null)
            {
                profileCollection = new ModProfile[0];
            }

            // init vars
            int displayCount = profileCollection.Count;
            ModProfile[] displayProfiles = new ModProfile[displayCount];
            ModStatistics[] displayStats = new ModStatistics[displayCount];
            List<int> missingStatsData = new List<int>(displayCount);

            // build arrays
            for(int i = 0;
                i < displayCount;
                ++i)
            {
                ModProfile profile = profileCollection[i];
                ModStatistics stats = null;

                if(profile != null)
                {
                    stats = ModStatisticsRequestManager.instance.TryGetValid(profile.id);

                    if(stats == null)
                    {
                        missingStatsData.Add(profile.id);
                    }
                }

                displayProfiles[i] = profile;
                displayStats[i] = stats;
            }

            // display
            modContainer.DisplayMods(displayProfiles, displayStats);

            // fetch missing stats
            if(missingStatsData.Count > 0)
            {
                ModStatisticsRequestManager.instance.RequestModStatistics(missingStatsData,
                (statsArray) =>
                {
                    if(this != null
                       && modContainer != null)
                    {
                        // verify still valid
                        bool doPushStats = (displayProfiles.Length == modContainer.modProfiles.Length);
                        for(int i = 0;
                            doPushStats && i < displayProfiles.Length;
                            ++i)
                        {
                            // check profiles match
                            ModProfile profile = displayProfiles[i];
                            doPushStats = (profile == modContainer.modProfiles[i]);

                            if(doPushStats
                               && profile != null
                               && displayStats[i] == null)
                            {
                                // get missing stats
                                foreach(ModStatistics stats in statsArray)
                                {
                                    if(stats != null
                                       && stats.modId == profile.id)
                                    {
                                        displayStats[i] = stats;
                                        break;
                                    }
                                }
                            }
                        }

                        // push display data
                        if(doPushStats)
                        {
                            modContainer.DisplayMods(displayProfiles, displayStats);
                        }
                    }
                },
                WebRequestError.LogAsWarning);
            }
        }

        // ----------[ PAGE TRANSITIONS ]---------
        public void InitiateTargetPageTransition(PageTransitionDirection direction, Action onTransitionCompleted)
        {
            if(!m_isTransitioning)
            {
                float containerWidth = ((RectTransform)this.m_currentPageContainer.transform.parent).rect.width;
                float mainPaneTargetX = containerWidth * (direction == PageTransitionDirection.FromLeft ? 1f : -1f);
                float transPaneStartX = mainPaneTargetX * -1f;

                this.m_currentPageContainer.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
                this.m_targetPageContainer.GetComponent<RectTransform>().anchoredPosition = new Vector2(transPaneStartX, 0f);

                StartCoroutine(TransitionPageCoroutine(mainPaneTargetX, transPaneStartX,
                                                       this.pageTransitionTimeSeconds, onTransitionCompleted));
            }
            #if DEBUG
            else
            {
                Debug.LogWarning("[mod.io] ModPages are already transitioning.");
            }
            #endif
        }

        private IEnumerator TransitionPageCoroutine(float mainPaneTargetX, float transitionPaneStartX,
                                                    float transitionLength, Action onTransitionCompleted)
        {
            m_isTransitioning = true;

            this.m_targetPageContainer.gameObject.SetActive(true);

            float transitionTime = 0f;

            // transition
            while(transitionTime < transitionLength)
            {
                float transPos = Mathf.Lerp(0f, mainPaneTargetX, transitionTime / transitionLength);

                this.m_currentPageContainer.GetComponent<RectTransform>().anchoredPosition = new Vector2(transPos, 0f);
                this.m_targetPageContainer.GetComponent<RectTransform>().anchoredPosition = new Vector2(transPos + transitionPaneStartX, 0f);

                transitionTime += Time.unscaledDeltaTime;

                yield return null;
            }

            // flip
            var tempContainer = this.m_currentPageContainer;
            this.m_currentPageContainer = this.m_targetPageContainer;
            this.m_targetPageContainer = tempContainer;

            var tempPage = modPage;
            this.m_modPage = this.m_targetPage;
            this.m_targetPage = tempPage;

            // finalize
            this.m_currentPageContainer.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
            this.m_targetPageContainer.gameObject.SetActive(false);

            m_isTransitioning = false;

            // notify, etc
            this.UpdatePageButtonInteractibility();
            if(this.onModPageChanged != null)
            {
                this.onModPageChanged.Invoke(this.m_modPage);
            }

            if(onTransitionCompleted != null)
            {
                onTransitionCompleted();
            }

        }

        // ---------[ UTILITY ]---------
        public void ClearAllFilters()
        {
            // Check if already cleared
            if(this.nameFieldFilter == null
               && this.tagMatchFieldFilter == null
               && this.GetSortMethod().Equals(this.defaultSortMethod))
            {
                return;
            }

            // Clear
            this.m_requestFilter = new RequestFilter()
            {
                sortFieldName = this.defaultSortMethod.fieldName,
                isSortAscending = this.defaultSortMethod.ascending,
            };

            this.Refresh();

            // notify
            if(this.onRequestFilterChanged != null)
            {
                this.onRequestFilterChanged.Invoke(this.m_requestFilter);
            }
        }

        /// <summary>Sets the mod page, updates displays, and notifies event listeners.</summary>
        protected void DisplayModPage(RequestPage<ModProfile> newModPage)
        {
            if(this.m_modPage != newModPage)
            {
                this.m_modPage = newModPage;

                this.UpdateCurrentPageDisplay();
                this.UpdatePageButtonInteractibility();

                if(this.onModPageChanged != null)
                {
                    this.onModPageChanged.Invoke(newModPage);
                }
            }
        }

        // ---------[ OBSOLETE ]---------
        [Obsolete("Use ExplorerView.containerTemplate instead.")][HideInInspector]
        public RectTransform pageTemplate = null;
        [Obsolete("Use ExplorerView.containerTemplate instead.")][HideInInspector]
        public GameObject itemPrefab = null;
        [Obsolete("Use ExplorerView.defaultSortMethod instead.")][HideInInspector]
        public string defaultSortString = string.Empty;

        [Obsolete("Use PageNumberDisplay component instead.")][HideInInspector]
        public Text pageNumberText;
        [Obsolete("Use PageCountDisplay component instead.")][HideInInspector]
        public Text pageCountText;
        [Obsolete("Use ResultCountDisplay component instead.")][HideInInspector]
        public Text resultCountText;

        [Obsolete("No longer supported.")][HideInInspector]
        public RectTransform currentPageContainer;
        [Obsolete("No longer supported.")][HideInInspector]
        public RectTransform transitionPageContainer;
        [Obsolete("No longer supported.")][HideInInspector]
        public GridLayoutGroup gridLayout;

        [Obsolete("Use ExplorerView.modPage instead.")]
        public RequestPage<ModProfile> currentPage
        {
            get { return this.m_modPage; }
            set
            {
                if(this.m_modPage != value)
                {
                    this.m_modPage = value;

                    if(this.onModPageChanged != null)
                    {
                        this.onModPageChanged.Invoke(this.m_modPage);
                    }
                }
            }
        }

        [Obsolete("No longer necessary.")][HideInInspector]
        public RectTransform contentPane
        {
            get
            {
                if(this.m_currentPageContainer != null)
                {
                    return this.m_currentPageContainer.transform.parent as RectTransform;
                }
                return null;
            }
            set {}
        }

        [Obsolete]
        public int itemsPerPage
        {
            get
            {
                return this.containerTemplate.itemLimit;
            }
        }

        [Obsolete("No longer supported.")]
        public IEnumerable<ModView> modViews
        {
            get
            {
                if(this.m_currentPageContainer != null)
                {
                    return this.m_currentPageContainer.GetModViews();
                }
                return null;
            }
        }

        [Obsolete("Use ExplorerView.modPage.CalculatePageIndex() instead.")]
        public int CurrentPageNumber
        {
            get
            {
                if(this.modPage != null)
                {
                    return 1+this.modPage.CalculatePageIndex();
                }

                return 0;
            }
        }
        [Obsolete("Use ExplorerView.modPage.CalculatePageCount() instead.")]
        public int CurrentPageCount
        {
            get
            {
                if(this.modPage != null)
                {
                    return this.modPage.CalculatePageCount();
                }
                return 0;
            }
        }

        [Obsolete("No longer supported. Use ExplorerView.onRequestFilterChanged instead.", true)]
        public event Action<string[]> onTagFilterUpdated;

        [Obsolete("No longer necessary. Initialization occurs in Start().")]
        public void Initialize() {}

        [Obsolete("No longer necessary. Event is directly linked to ModBrowser.")]
        public event Action<ModView> inspectRequested;
        [Obsolete("No longer necessary. Event is directly linked to ModBrowser.")]
        public void NotifyInspectRequested(ModView view)
        {
            if(inspectRequested != null)
            {
                inspectRequested(view);
            }
        }

        [Obsolete("No longer necessary. Event is directly linked to ModBrowser.")]
        public event Action<ModView> subscribeRequested;
        [Obsolete("No longer necessary. Event is directly linked to ModBrowser.")]
        public void NotifySubscribeRequested(ModView view)
        {
            if(subscribeRequested != null)
            {
                subscribeRequested(view);
            }
        }

        [Obsolete("No longer necessary. Event is directly linked to ModBrowser.")]
        public event Action<ModView> unsubscribeRequested;
        [Obsolete("No longer necessary. Event is directly linked to ModBrowser.")]
        public void NotifyUnsubscribeRequested(ModView view)
        {
            if(unsubscribeRequested != null)
            {
                unsubscribeRequested(view);
            }
        }

        [Obsolete("No longer necessary. Event is directly linked to ModBrowser.")]
        public event Action<ModView> enableModRequested;
        [Obsolete("No longer necessary. Event is directly linked to ModBrowser.")]
        public void NotifyEnableRequested(ModView view)
        {
            if(enableModRequested != null)
            {
                enableModRequested(view);
            }
        }

        [Obsolete("No longer necessary. Event is directly linked to ModBrowser.")]
        public event Action<ModView> disableModRequested;
        [Obsolete("No longer necessary. Event is directly linked to ModBrowser.")]
        public void NotifyDisableRequested(ModView view)
        {
            if(disableModRequested != null)
            {
                disableModRequested(view);
            }
        }

        [Obsolete("Use ExplorerView.SetSortMethod() instead.")]
        public void SetSortString(string sortString)
        {
            if(sortString == null) { sortString = string.Empty; }

            // set vars
            string fieldName = sortString;
            bool ascending = true;

            // check for descending
            if(sortString.StartsWith("-"))
            {
                ascending = false;

                if(sortString.Length > 1)
                {
                    fieldName = sortString.Substring(1);
                }
                else
                {
                    fieldName = string.Empty;
                }
            }

            this.SetSortMethod(ascending, fieldName);
        }

        [Obsolete("Use ExplorerView.GetSortMethod() instead.")]
        public string GetSortString()
        {
            string sortString = (this.m_requestFilter.isSortAscending ? "" : "-")
                + this.m_requestFilter.sortFieldName;

            return sortString;
        }

        [Obsolete]
        public RequestFilter GenerateRequestFilter()
        {
            return this.m_requestFilter;
        }
    }
}
