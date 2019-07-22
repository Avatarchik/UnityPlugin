using UnityEngine;

namespace ModIO.UI
{
    /// <summary>A view that provides information to child IModfileViewElements.</summary>
    public class ModfileView : MonoBehaviour
    {
        // ---------[ FIELDS ]---------
        /// <summary>Event fired when the modfile changes.</summary>
        public event System.Action<Modfile> onModfileChanged;

        /// <summary>Currently displayed modfile.</summary>
        [SerializeField]
        private Modfile m_modfile = null;

        // --- Accessors ---
        /// <summary>Currently displayed modfile.</summary>
        public Modfile modfile
        {
            get { return this.m_modfile; }
            set
            {
                if(this.m_modfile != value)
                {
                    this.m_modfile = value;

                    if(this.onModfileChanged != null)
                    {
                        this.onModfileChanged(this.m_modfile);
                    }
                }
            }
        }

        // ---------[ INITIALIZATION ]---------
        protected virtual void Awake()
        {
            #if DEBUG
            ModfileView nested = this.gameObject.GetComponentInChildren<ModfileView>(true);
            if(nested != null && nested != this)
            {
                Debug.LogError("[mod.io] Nesting ModfileViews is currently not supported due to the"
                               + " way IModfileViewElement component parenting works."
                               + "\nThe nested ModfileViews must be removed to allow ModfileView functionality."
                               + "\nthis=" + this.gameObject.name
                               + "\nnested=" + nested.gameObject.name,
                               this);
                return;
            }
            #endif

            // assign modfile view elements to this
            var modfileViewElements = this.gameObject.GetComponentsInChildren<IModfileViewElement>(true);
            foreach(IModfileViewElement viewElement in modfileViewElements)
            {
                viewElement.SetModfileView(this);
            }
        }
    }
}