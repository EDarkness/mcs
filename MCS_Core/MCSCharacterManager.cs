using UnityEngine;
using System;
//using System.Collections;
using System.Collections.Generic;
using System.Linq;
//using System.Xml;
using System.Xml.Serialization;
//using System.Text;
using System.IO;
using UnityEditor;


using MCS.CONSTANTS;
using MCS.FOUNDATIONS;
using MCS.COSTUMING;
using MCS.CORESERVICES;
using MCS.SERVICES;
using MCS_Utilities.Morph;

namespace MCS
{
	/*
    /// <summary>
	/// CharacterManager is a subclass of Unity's MonoBehavior and is automatically attached
    /// to MCS Figures when they are loaded into the scene. Every MCS figure will have its own instance of CharacterManager.
    /// 
    /// THe CharacterManager provides access to the majority of the API that game developers will need when 
    /// working with MCS. To get a reference to the CharacterManager on an MCS figure in a MonoBehaviour attached to an MCS Figure
    /// use Monobehaviours GetComponent method.
    /// 
    ///     CharacterManager m_CharacterManager = GetComponent<CharacterManager>();
    ///     
	/// </summary>
    */ 
    

	[ExecuteInEditMode]
	public class MCSCharacterManager : MonoBehaviour //, ISerializationCallbackReceiver
	{
		
        //public variables**********
        //
		public static float Version = 1.9f;                             //version number variables
        public static int Major = 1;
        public static int Minor = 9;
		public static int Revision = 2;
                                                                        //indent marker******
        public bool autoUpdateModel = true;                             //this flag will set the model to automatically update itself every frame
                                                                        //and is set to do so when model is created.  Can be changed to manual by
                                                                        //setting this value to false when created.

        public bool autoUpdateModelLate = true;                         //this flag will set the model to automatically do the late update for all
                                                                        //parts related to this character.  Setting this value to false will allow
                                                                        //for manual frame updating of this entity.
        
        public delegate void PreLODChange(float level, SkinnedMeshRenderer activeFigure);
        public delegate void PostLODChange(float level, SkinnedMeshRenderer activeFigure, bool figureChanged);
        public Animator anim;                                           //Backing reference to the GameObject (MCS Figure's) Animator component for
                                                                        //the public GetAnimatorManager method.

        protected Dictionary<string, float> CurrentJCTBlendshapes;      //Dictionary of blendshape names and values that are attached and require 
                                                                        //JCT service.

        public bool HasClonedMeshes = false;                            //we set this to true if we are using a JCT morph and looped through all our 
                                                                        //core meshes and duplicated the meshes (we need to do this for JCT morphed
                                                                        //avatars).  True if there are cloned meshes for the JCT service.

        public JCTTransition jct;                                       //Backing reference to the JCTTransition for the GetJctManager method
        public delegate void MCSCMBlendshapeValueChange();              //Delegate and event for raising whenever a Blendshape value changes.
        public event MCSCMBlendshapeValueChange OnCMBlendshapeValueChange;  //Event broadcast whenever a Blendshape value on the figure has changed. 
                                                                        //Subscribe to this event to find out whenever a Blendshape value has changed.

        internal bool _isAwake = false;                                 //Internal backing refercne for isAwake public property
        internal CostumeModel _clothingModel;                           //Internal. Our reference to the clothing CostumeModel.
        internal CostumeModel _propModel;                               //Internal. Our reference to the props CostumeModel.
        internal CostumeModel _hairModel;                               //Internal. Our reference to the hair CostumeModel.
        internal List<CIattachmentPoint> attachmentPoints = new List<CIattachmentPoint>();  /// Internal. The List of type CIattachmentPoint, 
                                                                        //containing all attachment points for this figure.
        public float currentLODLevel = 1f;                              //The current LOD level expressed as a scalar value.
        public COSTUME_BOUNDS_UPDATE_FREQUENCY CostumeBoundsUpdateFrequency = COSTUME_BOUNDS_UPDATE_FREQUENCY.NEVER;    /// Determins how frequently, 
                                                                        //if ever, will we update the bounds of a costume item. Default = NEVER

        public bool ResetBlendshapesOnStart = true;                     //Do we reset morphs on start, if yes, it means all blendshapes on all SMR 
                                                                        //are zero'd out, this is needed for state serialization when you have 2
                                                                        //figures using the same fbx with different morphs.  NOTE: this should only 
                                                                        //be false for 2 reasons, 1: you don't want the performance cost of reseting 
                                                                        //these (not a high cost though), 2: you have custom blendshapes that aren't 
                                                                        //managed by MCS

        public bool ForceJawShut = false;                               //if using animations that don't close the mounth, this can be set to force it
        public delegate void OnPostSetBlendshapeValueAsync();           //Delegate type used as a callback in SetBlendshapeValueAsync
        public bool testBool = false;
        public bool saDone = false;                                     //flag for letting the body know that we've already done the streaming assets update for this model
        public String streamBase = "/StreamingAssets/";                 //base streaming assets location
        public String sDataLoc = "MCSFemale/MCSFemale/MCSFemale_LOD0";  //location of streaming data for this mesh (starts with female model)
        public CostumeItem costume;                                     //used for storing costume export information
        //public String saveLoc;                                          //save/load location used by the editor

        
        //private variables**********
        //
        private CoreMorphs _coreMorphs;                                 //Backing reference for the public coreMorphs property
        private bool _needsToTriggerPostMorph = false;
        private AlphaInjectionManager _alphaInjection;                  //Internal. Our AlphaInjectionManager reference. Every figure has it's own 
                                                                        //AlphaInjectionManager which handles alpha textures to hide items beneath others.
        private CIbody _figureMesh;                                     //Internal. Our reference to the body mesh for this figure.

        [SerializeField][HideInInspector]
		private HashSet<string> _blendShapeChanges;                     //Queues to actions on the character.  Internal. HashSet of temporary blendshape 
                                                                        //changes.
                                                                        //<remarks>Anyone know why we'd want to serialize temporary change data?</remarks>
		private Dictionary<string, bool> _clothingChanges;              //Backing reference for the <see cref="clothingChanges"/> property.
        private CSBoneService _boneService;                             //Internal. Our reference to the CSBoneService component for this figure.
        private Transform _jawBone;
        private Dictionary<int, StreamingMorphs.InjectMorphNamesIntoFigureAsyncResult> _queuedInjection = null;
        private Morph[] _queuedMorphs = null;
        private OnPostSetBlendshapeValueAsync _queuedOnPostSetBlendshapeValueAsync = null;
        private bool _initialized = false;
        private bool defaults_init = false;                             //did we set up the defaults to be added to the SMR (only need to do this once)


        //Event variables
        public event PreLODChange OnPreLODChange;                       //Fired before SetLODLevel
        public event PostLODChange OnPostLODChange;                     //Fired after SetLODLevel

        
        //  A formatted string with the version number of this CharacterManager. 
        //  The format of the is of the form: {Major}.{Minor}.{Revision}
		public string VersionInfo {

			get {
				return string.Format("{0}.{1}.{2}", MCSCharacterManager.Major, MCSCharacterManager.Minor, MCSCharacterManager.Revision);
			}

		}


        /// <summary>
        /// Returns a reference to MCS Figure's Animator Component.
        /// </summary>
		public Animator GetAnimatorManager() {

			if (anim == null) {
				anim = gameObject.GetComponent<Animator> ();
			}
			return anim;
		}

        
        /// <summary>
        /// Returns a reference to the JCTTransition for this MCS Figure that this CharacterManager is component of.
        /// </summary>
		public JCTTransition GetJctManager() {

			if (jct == null)
				jct = gameObject.GetComponentInChildren<JCTTransition> ();
			return jct;

		}
        
		
		/// <summary>
		/// Internal. Broadcasts the blendshape value change event to all subscribed methods.
		/// </summary>
		private void BroadcastBlendshapeValueChange ()
		{
			if (OnCMBlendshapeValueChange != null)
				OnCMBlendshapeValueChange ();
		}

        
        /// <summary>
        /// True if the Awake() method has been called by the Unity Engine.
        /// </summary>
        public bool isAwake {

            get
            {
                return _isAwake;
            }

        }

        
        /// <summary>
        /// Reference to the CoreMorphs for this CharacterManager
        /// Use coreMorphs to gain access to individual Morphs on the figure.
        /// </summary>
        public CoreMorphs coreMorphs
        {
            get
            {
                if(_coreMorphs == null && isAwake)
                {
                    _coreMorphs = gameObject.GetComponent<CoreMorphs>();
                    if (_coreMorphs == null)
                    {
                        _coreMorphs = gameObject.AddComponent<CoreMorphs>();
                    }
                    _coreMorphs.SetRootObject(gameObject);
                }

                return _coreMorphs;
            }
        }

        
        public void OnPostJCT()
        {
            if (_needsToTriggerPostMorph)
            {
                ResyncBounds();
                _needsToTriggerPostMorph = false;
            }

            if (ForceJawShut)
            {
                ShutJaw();
            }
        }

        /// <summary>
        /// Internally track if we need to do anything after a morph event
        /// </summary>
        private void OnPostMorph()
        {
            if(CostumeBoundsUpdateFrequency == COSTUME_BOUNDS_UPDATE_FREQUENCY.ON_MORPH)
            {
                _needsToTriggerPostMorph = true;
                //ResyncBounds();
            }
        }


		/// <summary>
		/// Internal. Accessor and initialiser for the reference to the clothing CostumeModel.
		/// </summary>
		/// <value>The clothing model.</value>
		internal CostumeModel clothingModel
		{
			get {
				if (_clothingModel == null)
					_clothingModel = new CostumeModel (false);
				return _clothingModel;
			}
		}


		/// <summary>
		/// Internal. Accessor and initialiser for the reference to the props CostumeModel.
		/// </summary>
		/// <value>The props model.</value>
		internal CostumeModel propModel
		{
			get {
				if (_propModel == null)
					_propModel = new CostumeModel ();
				return _propModel;
			}
		}


		/// <summary>
		/// Internal. Accessor and initialiser for the reference to the hair CostumeModel.
		/// </summary>
		/// <value>The hair model.</value>
		internal CostumeModel hairModel
		{
			get {
				if (_hairModel == null)
					_hairModel = new CostumeModel (true);
				return _hairModel;
			}
		}


		/// <summary>
		/// Internal. Our ContentPackModel reference. Every figure has a ContentPackModel, which contains a List of type ContentPack containing all the packs associated with the figure.
		/// </summary>
		[SerializeField]internal ContentPackModel _contentPackModel;
		internal ContentPackModel contentPackModel{
			get{
				if(_contentPackModel == null)
					_contentPackModel = new ContentPackModel();
				return _contentPackModel;
			}
		}


		/// <summary>
		/// Internal. Accessor and initialiser for the reference to the AlphaInjectionManager for this figure.
		/// </summary>
		/// <value>Returns a reference to the AlphaInjectionManager for this figure.</value>
		public AlphaInjectionManager alphaInjection
		{
			get {
                if (_alphaInjection == null)
                {
                    //alpha injection should only be handled if it's in the scene
                    if (!isActiveAndEnabled)
                    {
                        return null;
                    }

                    //_alphaInjection = new AlphaInjectionManager();

                    _alphaInjection = gameObject.GetComponent<AlphaInjectionManager>();
                    if (_alphaInjection == null)
                    {
                        _alphaInjection = gameObject.AddComponent<AlphaInjectionManager>();
                    }
                }
				return _alphaInjection;
			}
		}


		/// <summary>
		/// Internal. Accessor and initialiser for the reference to the body mesh for this figure.
		/// </summary>
		/// <value>The figure mesh.</value>
		internal CIbody figureMesh
		{
			get {
                if (_figureMesh == null)
                {
                    _figureMesh = GetComponentInChildren<CIbody>();
                }
				return _figureMesh;
			}
		}


		/// <summary>
		/// Internal. Accessor and initialiser for the HashSet of temporary blendshape changes.
		/// </summary>
		/// <value>The blend shape changes.</value>
		internal HashSet<string> blendShapeChanges
		{
			get {
				if (_blendShapeChanges == null)
					_blendShapeChanges = new HashSet<string> ();
				return _blendShapeChanges;
			}
		}



//		private List<string> _blendShapeChanges;
//		internal List<string> blendShapeChanges{
//			get{
//				if(_blendShapeChanges == null)
//					_blendShapeChanges = new List<string>();
//				return _blendShapeChanges;
//			}
//		}


		/// <summary>
		/// Internal. Accessor and initialiser for the dictionary of clothing changes.
		/// </summary>
		/// <value>The clothing changes.</value>
		internal Dictionary<string, bool> clothingChanges
		{
			get {
				if (_clothingChanges == null)
					_clothingChanges = new Dictionary<string, bool> ();
				return _clothingChanges;
			}
		}


		// Helpers
		// internal bool IsChangingBlendshapes { get { return (blendShapeChanges != null && blendShapeChanges.Count > 0); } }
		// internal bool IsChangingClothing { get { return (clothingChanges != null && clothingChanges.Count > 0); } }


		/// <summary>
		/// Internal. Accesssor and initialiser for the CSBoneService component reference.
		/// </summary>
		/// <value>The bone service.</value>
		private CSBoneService boneService {
		
			get {
                if (_boneService == null)
                {
                    _boneService = gameObject.GetComponentInChildren<CSBoneService>();

                    /*
                    if(_boneService == null)
                    {
                        ConfigureSkeletonRuntime();
                    }
                    */
                }
				return _boneService;
			}
		}

        
		// for easy testing
		// InspectorButton("initCharacterManager")] 
		// public bool reloadCharacter = false;


		/// <summary>
		/// MonoBehaviour Awake event. Raised on instance creation.
		/// </summary>
		void Awake ()
		{
            _isAwake = true;
			//initCharacterManager ();//should only get called when pressing play

		}



		/// <summary>
		/// MonoBehaviour Start event. Raised on instance startup.
		/// </summary>
		void Start() {
		
            if (coreMorphs != null)
            {
                coreMorphs.OnPostMorph += OnPostMorph;
            }

            //get the jct if it's not already fetched
            if (jct == null)
            {
                jct = GetJctManager();
            }
            if(jct != null)
            {
                jct.OnPostJCT += OnPostJCT;
            }
                                                                        //indent marker
		//	Debug.Log ("pre init");
			CoreMesh[] all_clothing = GetComponentsInChildren<CoreMesh>(true);
			if(all_clothing != null){
				foreach(CoreMesh cloth in all_clothing){
					if(cloth.ID != null){
						if(cloth.ID.Contains("G2FSimplifiedEyes")){
							cloth.GetRuntimeMesh();
						}
					}
				}
			}
            CurrentJCTBlendshapes = new Dictionary<string, float>();

            initCharacterManager(true);                                 //should only get called when pressing play
            
			//we need to sync regardless if we're in the application or the editor, otherwise the lods and blendshapes get out of sync
			SyncAllBlendShapes();
            SyncHairOverlays();

		}


        /// <summary>
        /// MonoBehaviour Update event. Listen for Queue events
        /// </summary>
        void Update () {
	    
            //checking for automatic updates
            //Note: Models only auto update if the autoUpdateModel flag is true.  If you want to control when updates happen, then
            //      turn off automatic updating and call FrameUpate() manually.
            if (autoUpdateModel)
                FrameUpdate();

		}
                                                                        //indent marker*****
        //Method:   FrameUpdate()
        //Note:     Called every frame to perform every action of this character.  Every update that used to be independent is now
        //          handled directly by this method.  It'll need to be called for every character in the game if done manually.
        //          All of the other parts of the model connect to this method to be updated.  The FrameUpdate() of each part can
        //          be called separately if necessary.
        public void FrameUpdate() {

            ApplyQueuedInjections();
            ApplyClothingChanges();                                     //updates clothing information, checks happen in method

            int totPoints = attachmentPoints.Count;                     //get the number of attachment points on this model

            //cycle through all of the attachment points and update them
            for (int i = 0; i < totPoints; i++) {

                attachmentPoints[i].FrameUpdate();                      //update each attachement point each frame

             }

            totPoints = clothingModel.availableItems.Count;             //get the total number of items on this model

            //cycle through all of the clothing on this model update only what is visible
            for (int i = 0; i < totPoints; i++) {

                if (clothingModel.availableItems[i].isVisible) {
                    
                    clothingModel.availableItems[i].FrameUpdate();      //if it's visible, then we'll update it this frame

                }

            }

            totPoints = hairModel.availableItems.Count;                 //get the total number of hair models on this character

            //cycle through all of the hair models attached to this character, update only what is visible
            for (int i = 0; i < totPoints; i++) {

                if (hairModel.availableItems[i].isVisible) {

                    hairModel.availableItems[i].FrameUpdate();          //if it's visible, then we'll update the hair for this frame

                }

            }

            totPoints = propModel.availableItems.Count;                 //get the total number of props associated with this model

            //cycle through all of the attached props and update only what is visible
            for (int i = 0; i < totPoints; i++) {

                if (propModel.availableItems[i].isVisible) {

                    propModel.availableItems[i].FrameUpdate();          //if this prop is visible, then update it for this frame

                }

            }

        }


        void LateUpdate()
        {
            //checking for automatic updates
            //Note: Models only auto update if the autoUpdateModelLate flag is true.
            if (autoUpdateModelLate)
                FrameLateUpdate();

        }


        //Method:   FrameLateUpdate()
        //Notes:    Just like FrameUpdate(), this method can be called whenever a late update is required.  It will automatically
        //          update itself when autoUpdateModelLate is true.
        public void FrameLateUpdate() {

            alphaInjection.Process();                                   //final update of this character, eats a lot of frame time
            
        }


        protected Transform jawBone
        {
            get
            {
                if(_jawBone == null)
                {
                    _jawBone = boneService.GetJawBone();
                }
                return _jawBone;
            }
        }

        /// <summary>
        /// Forces the jaw bone to be shut, fires after animation and jct
        /// </summary>
        public void ShutJaw() {
        
            jawBone.localRotation = Quaternion.identity;

        }

        //This is the easiest way to close the jaw bone, but requires your animation controller to have the IK pass enabled
        /*
        void OnAnimatorIK()
        {
            if (EnableShutJaw)
            {
                Animator anim = GetComponent<Animator>();
                anim.SetBoneLocalRotation(HumanBodyBones.Jaw, Quaternion.identity);
            }
        }
        */


        /// <summary>
        /// Clears all morphs from the figure and clothing
        /// </summary>
        public void RemoveAllMorphs()
        {
            coreMorphs.DetachAllMorphs();
            coreMorphs.Resync();
			SyncMorphsToJCT ();

        }


		/// <summary>
		/// Syncs all blendshapes on the figure, clothing and hair.
		/// </summary>
		public void SyncAllBlendShapes ()
		{
            coreMorphs.Resync();
			SyncMorphsToJCT();

            //TODO: this should be called BroadcastMorphValueChange
			BroadcastBlendshapeValueChange();
		}


        /// <summary>
        /// Returns an Array of Morph objects who's value is non-zero.
        /// </summary>
		public Morph[] GetActiveBlendShapes(){
			List<Morph> morphs = new List<Morph>();
			foreach (Morph morph in coreMorphs.morphStateGroups["Attached"]) {
				if (morph.value > 0f) {
					morphs.Add (morph);
				}
			}

			return morphs.ToArray ();
		}


        /// <summary>
        /// Auto attaches and drives a morph from a range of 0-100f where 0 is no morph and 100 is full morph
        /// </summary>
        /// <param name="morphName">The "localName" of a morph, eg: FBMHeavy</param>
        /// <param name="value">The value between 0-100f</param>
        /// <returns>True on success, false on failure</returns>
		public bool SetBlendshapeValue(string morphName, float value) {

			Morph[] morphs = new Morph[1];
            morphs[0] = new Morph();
            morphs [0].localName = morphName;
			morphs [0].value = value;

			if (!coreMorphs.morphLookup.ContainsKey(morphName)) {
				return false;
			}

            //tell coreMorphs that we need to attach a new morph
			coreMorphs.AttachMorphs(morphs);
            //sync these values to the meshes (as in, drive the real blendshapes)
			coreMorphs.SyncMorphValues(morphs);
            //sync values with our jct bone handler
			SyncMorphsToJCT(morphs);

            //our values changed, raise the event that tells other services we changed too
            if(OnCMBlendshapeValueChange != null)
            {
                OnCMBlendshapeValueChange();
            }
            
            return true;
		}
                                                                        //indent marker
        /// <summary>
        ///NOTE:    When a MCS model is first inserted into a scene, the morphs are not added
        ///         to the Skinned Mesh Rendere.  This is done whenever the morph is actually
        ///         adjusted or clicked on in the inspector.  What this means is access to
        ///         facial morphs through the Skinned Mesh Renderer isn't possible until it is
        ///         actually activated.  This function will activate all facial morphs (and
        ///         any other morphs needed by other methods) so that they end up on the
        ///         Skinned Mesh Renderer blendshape list.  At the moment, this includes all
        ///         facial morphs.
        ///
        /// </summary>
        public void SetDefaultBlendshapes() {

            //SkinnedMeshRenderer smr = gameObject.GetComponentInChildren<SkinnedMeshRenderer>();  //get the skinned mesh renderer for this object
            
            //if we've already setup the defaults, no reason to do it again
            if (defaults_init)
                return;

            //need to go over all of the morphs available
            for (int i = 0; i < coreMorphs.morphs.Count; i++) {

                //check to see if this morph is part of Emotion Control
                if (coreMorphs.morphs[i].localName.Contains("eCTRL")) {

                    //to avoid redundency, lets check to see if those blendshapes are there already
                    //we don't want to mess with the values that are already there
                    if (!coreMorphs.morphs[i].attached)
                        SetBlendshapeValue(coreMorphs.morphs[i].localName, 0);  //just need to add this to the list.
                    
                }

            }

            defaults_init = true;                                       //we've done it, so no need to do it again

        }

        
        private void ApplyQueuedInjections()
        {
            if(_queuedInjection == null || _queuedInjection.Count <= 0)
            {
                return;
            }

            StreamingMorphs sm = new StreamingMorphs();
            SkinnedMeshRenderer[] smrs = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach(SkinnedMeshRenderer smr in smrs)
            {
                int id = smr.GetInstanceID();
                if (!_queuedInjection.ContainsKey(id))
                {
                    continue;
                }

                foreach(MCS_Utilities.Morph.MorphData md in _queuedInjection[id].morphDatas)
                {
					if (md != null) {
						sm.InjectMorphDataIntoSMR (smr, md);
					}
                }

                //FIX for unity bug in 5.3
                Mesh m = smr.sharedMesh;
                smr.sharedMesh = m;
            }

            /*
            UnityEngine.Debug.Log("Attached");
            foreach(Morph m in _queuedMorphs)
            {
                UnityEngine.Debug.Log("Morph: " + m.localName);
            }
            */
            //sync these values to the meshes (as in, drive the real blendshapes)
            coreMorphs.SyncMorphValues (_queuedMorphs);
            //sync values with our jct bone handler
            SyncMorphsToJCT (_queuedMorphs);

            //our values changed, raise the event that tells other services we changed too
            if(OnCMBlendshapeValueChange != null)
            {
                OnCMBlendshapeValueChange();
            }

            if(_queuedOnPostSetBlendshapeValueAsync != null)
            {
                _queuedOnPostSetBlendshapeValueAsync();
            }

            _queuedOnPostSetBlendshapeValueAsync = null;
            _queuedMorphs = null;
            _queuedInjection.Clear();
        }

        
        /// <summary>
        /// Async method that automatically attaches and drives a morph from a range of 0-100f where 0 is no morph and 100 is full morph
        /// </summary>
        /// <param name="morphName">The "localName" of a morph eg. FBMHeavy</param>
        /// <param name="value">The value of the morph 0 - 100f</param>
        /// <param name="callback">Optional call back method run when the Blendshape value has been set.</param>
        public void SetBlendshapeValueAsync(string morphName, float value, OnPostSetBlendshapeValueAsync callback=null)
        {
            Morph[] morphs = new Morph[1];

            morphs[0] = new Morph();
            morphs [0].localName = morphName;
			morphs [0].value = value;

			if (!coreMorphs.morphLookup.ContainsKey (morphName)) {
				return ;
			}

            //tell coreMorphs that we need to attach a new morph
			coreMorphs.AttachMorphs(morphs,false,true,(Dictionary<int, StreamingMorphs.InjectMorphNamesIntoFigureAsyncResult> result) => {
                //UnityEngine.Debug.Log("Attached complete");

                _queuedMorphs = morphs;
                _queuedInjection = result;
                _queuedOnPostSetBlendshapeValueAsync = callback;
            });
        }


        /// <summary>
        /// Registers morph names and values with the JCT Manager
        /// </summary>
        /// <param name="morphs">\
        /// Array of Morph objects to sync. If Null all morphs from coreMorphs are synced.
        /// null by default
        /// </param>
		public void SyncMorphsToJCT(Morph[] morphs = null)
		{
			//This is a legacy function before we swap to 2.0 which will use an avatar definition

			if (morphs == null) {
				morphs = coreMorphs.morphStateGroups ["All"].ToArray();
			}

            JCTTransition jct = GetJctManager();
            if(jct == null)
            {
                UnityEngine.Debug.LogError("JCT is not available, can not sync morphs");
                return;
            }

			foreach (Morph m in morphs) {
                jct.SetMorphValue (m.localName, m.value/100f);
			}

            //kind of not necessary b/c we do the same thing with OnMCSCMBlendshapeValueChange
            jct.WakeUp();
        }


		/// <summary>
		/// Inits the character manager. We may use this several times, like in the importer
		/// </summary>
		void initCharacterManager (bool refresh=false)
		{
            //only initialize once, unless we force it
            if(_initialized && !refresh)
            {
                return;
            }
			//Debug.Log("INTING CHAR MAN: " + gameObject.name);

            if (Application.isPlaying)
				_clothingModel = new CostumeModel(false); //we'll repopulate it in the detect section below

			// if (propModel == null) 
			// 		propModel = new CostumeModel (this);
			
			// if (hairModel == null)
			// 		hairModel = new CostumeModel (true);

			// detect various costuming items that might be available - connected means its in the stack but bones may not be attached
			DetectAttachedHair();
			DetectAttachedProps();
			DetectAttachmentPoints(); //make sure all props are available before we go activating attachmentpoints

			// ReloadClothingFromFigure ();
			DetectAttachedClothing();

            //we want to make sure our lod level is set before AI otherwise if we restore from a prefab we'll pick the wrong mats to recover
            SyncCurrentLODLevel();

            if (alphaInjection == null)
            {
                UnityEngine.Debug.LogWarning("Alpha injection component not found");
            }
            else
            {
                alphaInjection.SetFigureAndCostumeModel(figureMesh, clothingModel);
            }

            if (ResetBlendshapesOnStart)
            {
                ResetAllBlendshapes();
            }

            _initialized = true;
		}


        /// <summary>
        /// Finds all SMRs on figure and sets ALL blendshapes to 0, generally this is used to fix serialization issues and nothing more
        /// </summary>
        public void ResetAllBlendshapes()
        {
            //Debug.Log("ResetAllBlendshapes() called.");
            SkinnedMeshRenderer[] smrs = this.GetComponentsInChildren<SkinnedMeshRenderer>();
            //SkinnedMeshRenderer[] smrs = null;
            //if (smrs == null) Debug.Break();
            foreach (SkinnedMeshRenderer smr in smrs)
            {
                int total = smr.sharedMesh.blendShapeCount;
                for (int i = 0; i < total; i++)
                {
                    smr.SetBlendShapeWeight(i, 0f);
                }
            }

        }


		/// <summary>
		/// Reset everything on the character manager (not bundles)
		/// </summary>
		[ContextMenu ("Reset Everything")]
		void ResetModel()
		{
            _coreMorphs = null;
			_clothingModel = new CostumeModel (false);
			_propModel = new CostumeModel (this);
			_hairModel = new CostumeModel (true);
			attachmentPoints = new List<CIattachmentPoint> ();
			DetectAttachmentPoints();
			
			_clothingChanges = new Dictionary<string, bool> ();
		}


		/// <summary>
		/// Updates clothing visibility per GUI changes.
		/// </summary>
		/// <remarks>IAN: Does this need to be public?</remarks>
		internal void ApplyClothingChanges()
		{
			bool needed_update = (clothingChanges.Count > 0) ? true : false;

            var enumerator = clothingChanges.GetEnumerator();
            while (enumerator.MoveNext()) {

                string key = enumerator.Current.Key;
				if (clothingChanges[key]) {
					clothingModel.SetItemVisibility (key, true);
				} else {
					clothingModel.SetItemVisibility (key, false);
				}
			}

            if (needed_update == true)
            {
                coreMorphs.Resync();
                clothingChanges.Clear();
            }
		}


		/// <summary>
		/// Detects all CIattachmentpoints that are present
		/// and children of specific bones on the main
		/// skeleton of the figure.
		/// </summary>
		public void DetectAttachmentPoints()
		{
			// ensure we have an initialised list of attachment points
			if (attachmentPoints == null) {
				attachmentPoints = new List<CIattachmentPoint> ();
			}

			// removes all empty or non existant attachment points
			while (attachmentPoints.Remove (null));

			// gather a current list of all attachment points in this figure
			CIattachmentPoint[] aps = GetComponentsInChildren<CIattachmentPoint> (true);

			// add these gathered attachment points to the list
			foreach (CIattachmentPoint ap in aps) {
				if (attachmentPoints.Contains (ap) == false)
					attachmentPoints.Add (ap);
				ap.DetectAttachedProps ();
			}
		}


		/// <summary>
		/// Detects all CIclothing that are present, visible and
		/// children of the root Character Manager game object
		/// When unattached clothing is found it is attached
		/// and registerd with the character manager.
		/// </summary>
		public void DetectAttachedClothing()
		{
			CIclothing[] potential_clothing = gameObject.GetComponentsInChildren<CIclothing>(true);

			foreach (CIclothing cloth in potential_clothing) {

				if (cloth.isAttached == false) {
					AttachCIClothing (cloth, false); // just to makre sure thing are bound
				}

				clothingModel.AddItem (cloth);
				cloth.DetectCoreMeshes (); 
			}
		}


		/// <summary>
		/// Detects CIprops that are present, visible and
		/// children of registered attachment points. When
		/// detected the props are registered with the
		/// character manager.
		/// </summary>
		public void DetectAttachedProps()
		{
			// if (propModel == null)
			// 		propModel = new CostumeModel();

			CIprop[] potential_props = gameObject.GetComponentsInChildren<CIprop> (true);

			// some props can be added to model, so are instanatcnes of existing props in promodel, and some are instances that are stray
			foreach (CIprop prop in potential_props) {

				CIprop prop_to_add = null;

				// we need to make sure the prop exists in the availableProps null
				GameObject prop_holder = GetOrCreateAvailablePropsNullObject ();

				// this prop is instance
				if (prop.gameObject.transform.parent == prop_holder.transform) {
					prop_to_add = prop;
				} else {
					// this instance isn't in the right spot BUT it may be an instance
					CIprop[] specific_props = prop_holder.GetComponentsInChildren<CIprop> (true);
					bool is_instance = false;

					foreach (CIprop good_prop in specific_props) {
						if (good_prop.dazName == prop.dazName) {
							is_instance = true;
							// it's good, we shouldnt need to do anything
						}
					}

					if (is_instance == false) {
						// add prop to correct location and add to prop model
						prop_to_add = AttachCIProp (prop, true);
					}
				}

				if (prop_to_add != null) {
					prop_to_add.DetectCoreMeshes (); 
					propModel.AddItem (prop_to_add); // this auto handles duplicates
				}
			}
		}


		/// <summary>
		/// Detects CIhair that is present, visible and
		/// a child of the root character manager game
		/// object. When detected the CIhair will be
		/// registered with the character manager.
		/// </summary>
		public void DetectAttachedHair()
		{
			CIhair[] potential_hair = gameObject.GetComponentsInChildren<CIhair> (true);

			foreach (CIhair hair in potential_hair) {
				if (hair.isAttached == false)
					AttachCIHair (hair, false);
				
				hairModel.AddItem (hair);
				hair.DetectCoreMeshes ();
			}
		}


		/// <summary>
		/// As the name suggests this method returns the existing
		/// "Available Prop" null game object for a character manager.
		/// If said object doesn't exist, it is instantiated and returned.
		/// </summary>
		/// <returns>"AvailableProps" null game object.</returns>
		private GameObject GetOrCreateAvailablePropsNullObject()
		{
			// lets put unused props in a particular place
			Transform prop_holder_t = gameObject.transform.Find ("AvailableProps");
			GameObject prop_holder = null;

			if (prop_holder_t == null) {
				prop_holder = new GameObject ();
				prop_holder.name = "AvailableProps";
				prop_holder.transform.parent = transform;
				prop_holder.SetActive (false);
			} else {
				prop_holder = prop_holder_t.gameObject;
			}

			return prop_holder;
		}


		/// <summary>
		/// Attachs and binds a CIclothing item to the figure. If used in the Editor, the attachment is permanent. If used at runtime, the changes are lost at termination.
		/// </summary>
		/// <returns>The CIclothing added. Original or clone as defined with the boolean flag clone_item.</returns>
		/// <param name="clothing">Clothing.</param>
		/// <param name="clone_item">If set to <c>true</c> the CIclothing is cloned.</param>
		public CIclothing AttachCIClothing(CIclothing clothing, bool clone_item)
		{
			GameObject new_attached_instance = null;

            if (clone_item == true)
            {
                new_attached_instance = boneService.CloneAndAttachCostumeItemToFigure(clothing.gameObject);
            }
            else
            {
                new_attached_instance = boneService.AttachCostumeItemToFigure(clothing.gameObject);
            }
			
			CIclothing attached_cloth = new_attached_instance.GetComponent<CIclothing>();
			attached_cloth.isAttached = true;

			return attached_cloth;
		}


		/// <summary>
		/// Attachs and binds a CIhair item to the figure. If used in the Editor, the attachment is permanent. If used at runtime, the changes are lost at termination.
		/// </summary>
		/// <returns>The CIhair added. Original or clone as defined with the boolean flag clone_item.</returns>
		/// <param name="hair">The CIhair item to add.</param>
		/// <param name="clone_item">If set to <c>true</c> the CIhair is cloned.</param>
		public CIhair AttachCIHair(CIhair hair, bool clone_item)
		{
			GameObject new_attached_instance = null;

			if (clone_item == true)
				new_attached_instance = boneService.CloneAndAttachCostumeItemToFigure (hair.gameObject);
			else
				new_attached_instance = boneService.AttachCostumeItemToFigure (hair.gameObject);
			
			CIhair attached_hair = new_attached_instance.GetComponent<CIhair> ();
            attached_hair.isAttached = true;

			return attached_hair;
		}


		/// <summary>
		/// Attachs a CIprop to the figure. If used in the Editor, the attachment is permanent. If used at runtime, the changes are lost at termination.
		/// This is not the same as AttachmentPoints.
		/// </summary>
		/// <returns>The CIprop added. Original or clone as defined with the boolean flag clone_item.</returns>
		/// <param name="prop">The CIprop to add.</param>
		/// <param name="clone_item">If set to <c>true</c> the CIprop is cloned.</param>
		public CIprop AttachCIProp(CIprop prop, bool clone_item)
		{
			CIprop attached_prop = prop;
			GameObject new_attached_instance = prop.gameObject;

			// create a clone of the prop GameObject if required.
			if (clone_item == true)
				new_attached_instance = Instantiate (prop.gameObject); // boneService.CloneAndAttachCostumeItemToFigure(prop.gameObject);

            new_attached_instance.name = prop.name; //get rid of things like "(Clone)"

			// lets put unused props in a particular place
			GameObject prop_holder = GetOrCreateAvailablePropsNullObject ();

			new_attached_instance.transform.parent = prop_holder.transform;
			attached_prop = new_attached_instance.GetComponent<CIprop> ();

			// not sure how isAtatched works for props. i think it's likely not used at all, or only used with attachment points
			attached_prop.isAttached = true;

            //coreMorphs.Resync();

			return attached_prop;
		}


        /// <summary>
        /// Syncs attached CIClothing, CIProps and figure meshes with the
        /// current LOD level of the CharacterManager.
        /// </summary>
        /// <param name="level"></param>
        protected void SyncCurrentLODLevel(float level=-1)
        {
            if(level < 0)
            {
                level = currentLODLevel;
            }
            if(OnPreLODChange != null)
            {
                SkinnedMeshRenderer smr = figureMesh.GetSkinnedMeshRenderer();
                OnPreLODChange(level, smr);
            }
			clothingModel.SetItemLODLevel (level);
			propModel.SetItemLODLevel (level);
			hairModel.SetItemLODLevel (level);
			bool hasChanged = figureMesh.setLODLevel (level, true);
			SetAttachedPropLODLevels (level);
            currentLODLevel = level;
            if(OnPostLODChange != null)
            {
                SkinnedMeshRenderer smr = figureMesh.GetSkinnedMeshRenderer();
                OnPostLODChange(level, smr, hasChanged);
            }
        }


		/// <summary>
		/// Sets the LOD level on all character associated sub-geometries.
		/// </summary>
		/// <param name="level">The scalar level.</param>
		public void SetLODLevel (float level)
		{
            SyncCurrentLODLevel(level);
            coreMorphs.Resync();
            SyncHairOverlays();
		}


		/// <summary>
		/// Internal helper function that sets the LOD level
		/// for all cloned props on attachement points.
		/// </summary>
		/// <param name="level">The scalar level.</param>
		private void SetAttachedPropLODLevels(float level)
		{
			foreach (CIprop p in GetAllAttachedProps ()) {
				p.setLODLevel (level);
			}
		}


		/// <summary>
		/// Adds the given ContentPack to the ContentPackModel, and loads it onto the figure.
		/// </summary>
		/// <param name="content_pack">Content pack.</param>
		/// <remarks>THIS IS CONFUSING AND NEEDS TO BE ADDRESSED. There is no RemoveContentPack() function call.</remarks>
		public void AddContentPack(ContentPack content_pack)
		{
            //first thing we do is see if we're using 2018.4 or higher
            //#if UNITY_2018_4_OR_NEWER

            
            //#endif

			// add the content pack to the content pack model
			AddContentPackToModel(content_pack);
            LoadContentPackToFigure(content_pack);
		}


		/// <summary>
		/// Helper method for AddContentPack(). 
        /// 
        /// Should be internal or private. Should also be renamed to the less confusing AddContentPackToContentPackModel()
        /// Use AddContentPack(ConetntPack content_pack) instead.
		/// </summary>
		/// <param name="content_pack">The content pack to add to the ContentPackModel</param>
		public void AddContentPackToModel(ContentPack content_pack)
		{
			// the IF statement adds the content to the model
			if (contentPackModel.AddContentPack(content_pack) == false) {
				 Debug.LogWarning("Could not add content pack");
			}
		}


		/// <summary>
		/// Should be helper method for non-existant RemoveContentPack(). Should be internal or private. Should remove the content pack from the figure. Should also be renamed to the less confusing RemoveContentPackFromContentPackModel()
		/// </summary>
		/// <param name="obj">Object.</param>
		/// <param name="and_all_instances">If set to <c>true</c> all instances of the content pack are removed.</param>
		/// <remarks>THIS IS CONFUSING AND NEEDS TO BE ADDRESSED. There is no RemoveContentPack() function call.</remarks>
		public void RemoveContentPackFromModel(GameObject obj, bool and_all_instances = false)
		{
			if (contentPackModel.RemoveContentPack (obj) == true) {
			}
		}


        /// <summary>
        /// Unload and remove ALL content packs from the figure
        /// </summary>
        public void RemoveAllContentPacks()
        {
            //Create a copy, this is a bit safer then looping until the list is empty b/c there may be a bug in removing a content pack and we wouldn't leave the loop
            List<ContentPack> allPacks = new List<ContentPack>(GetAllContentPacks());
            for(int i=0;i<allPacks.Count;i++)
            {
                ContentPack cp = allPacks[i];
                RemoveContentPack(cp);
            }
        }


        /// <summary>
        /// Returns true if a content pack is in the available list of the figure
        /// </summary>
        /// <param name="cp"></param>
        public bool HasContentPack(ContentPack cp)
        {
            foreach(ContentPack currentCP in contentPackModel.availableContentPacks)
            {
                if (currentCP.name.Equals(cp.name))
                {
                    return true;
                }
            }

            return false;
        }


        /// <summary>
        /// Unload and remove the content pack from the figure
        /// </summary>
        /// <param name="cp"></param>
        public void RemoveContentPack(ContentPack cp)
        {
            UnloadContentPackFromFigure(cp);
            RemoveContentPackFromModel(cp.RootGameObject, true);

            SyncHairOverlays();
        }


        public void SyncHairOverlays()
        {
            bool foundHair = false;

            //do we no longer have hair? if so, remove the hair cap
            CIhair[] hairs = gameObject.GetComponentsInChildren<CIhair>(true);
            if (hairs.Length > 0)
            {
                CIhair hair = hairs[0];
                if (hair.isVisible)
                {
                    foundHair = true;
                    hair.SyncOverlay();
                }
            }

            if (!foundHair)
            {
                ClearHeadOverlay();
            }
        }


        public void MarkHairAsDirty()
        {
            CIhair[] hairs = gameObject.GetComponentsInChildren<CIhair>(true);
            for(int i = 0; i < hairs.Length; i++)
            {
                hairs[i].dirty = true;
            }
        }


		/// <summary>
		/// Returns a list of all ContentPacks in the ContentPackModel.
		/// </summary>
		/// <returns>List of type ContentPack.</returns>
		public List<ContentPack> GetAllContentPacks ()
		{
			return contentPackModel.GetAllPacks ();
		}


		/// <summary>
		/// Loads a given ContentPack onto the figure.
		/// </summary>
		/// <returns>The root of the ContentPack as a GameObject</returns>
		/// <param name="content_pack">The ContentPack to load.</param>
		public GameObject LoadContentPackToFigure(ContentPack content_pack)
		{
			GameObject root;
			Transform tempRoot;

            if(content_pack == null || content_pack.RootGameObject == null)
            {
                UnityEngine.Debug.LogWarning("Previous content pack is now null, skipping");
                return null;

            }

            //attempt to find an identically named object under the character manager
            // if we find it, use that game object as "root"
            // if we do not find it, create one under the character manager and assign root to the new object
			tempRoot = transform.Find (content_pack.RootGameObject.name);
            //UnityEngine.Debug.LogError("tempRoot: " + (tempRoot == null ? "null" : "not null"));

			if (tempRoot == null) {
				root = new GameObject ();
				root.name = content_pack.RootGameObject.name;
				root.transform.parent = transform;
			} else {
				root = tempRoot.gameObject;
			}

            List<GameObject> clones = new List<GameObject>();

			foreach (CIclothing cloth in content_pack.availableClothing) {

				GameObject cloth_clone = LoadClothingFromContentPackToFigure (cloth);

                if (boneService.IsBodyObject (cloth_clone.transform.parent.gameObject))
					cloth_clone.transform.parent = root.transform;

                clones.Add(cloth_clone);

            }
			
			foreach (CIhair hair in content_pack.availableHair) {

				GameObject hair_clone = LoadHairFromContentPackToFigure (hair);
			
				if (boneService.IsBodyObject(hair_clone.transform.parent.gameObject))
					hair_clone.transform.parent = root.transform;
                clones.Add(hair_clone);
			}
			
			foreach (CIprop prop in content_pack.availableProps) {
				GameObject prop_clone = LoadPropFromContentPackToFigure (prop);
                clones.Add(prop_clone);
			}

            //resync morphs
            //coreMorphs.Resync(clones);

            //resync lod
            //SetLODLevel(currentLODLevel);

            SyncMorphsToJCT();
            alphaInjection.invalidate();

            if (CostumeBoundsUpdateFrequency == COSTUME_BOUNDS_UPDATE_FREQUENCY.ON_ATTACH || CostumeBoundsUpdateFrequency == COSTUME_BOUNDS_UPDATE_FREQUENCY.ON_MORPH) {
                foreach(GameObject clone in clones)
                {
                    CostumeItem ci = clone.GetComponent<CostumeItem>();

                    //we can't recalculate right now because the morphs and jcts haven't been driven correctly
                    //ci.RecalculateBounds(true);
                    ci.MarkBoundsDirty();
                }

                /*
                foreach(CostumeItem ci in content_pack.availableItems)
                {
                    //ci.SetCharman(this);
                    //ci.RecalculateBounds();
                    ci.MarkBoundsDirty();
                }
                */
            }

            //Reset the position of the content pack so that if we were to call transform.position of the object we'd get the correct world position
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;


			if (root.transform.childCount < 1) {
				if (Application.isEditor)
					DestroyImmediate (root);
				else
					Destroy (root);
			}
			
			return root;
		}


        /// <summary>
        /// Removes the given Transform from the scene graph and by extension the Figure and destroys the GameObject
        /// 
        /// For best results use UnloadContentPackFromFigure
        /// </summary>
        /// <param name="trans">The transform of the GameObject to be removed.</param>
        protected void DestroyCP(Transform trans)
        {
            if(trans == null)
            {
                return;
            }
            if (trans.childCount < 1 && gameObject == trans.parent.gameObject) {
                trans.transform.parent = null; //remove from scene graph, so that if we add a CP back it won't be found here, this fixes when we do Destroy but not DestroyImmediate
                if (Application.isEditor)
                    DestroyImmediate (trans.gameObject);
                else
                    Destroy (trans.gameObject);
            }

        }


		/// <summary>
		/// Unloads the given ContentPack from the figure.
		/// </summary>
		/// <param name="content_pack">Content pack.</param>
		public void UnloadContentPackFromFigure(ContentPack content_pack)
		{			
			Transform parent = null;

			foreach (CIclothing cloth in content_pack.availableClothing) 
			{

                if(cloth == null)
                {
                    UnityEngine.Debug.LogWarning("CIclothing was null when attempting to remove");
                    clothingModel.RemoveItem(null);
                    continue;
                }

				CIclothing sceneCloth = GetClothingByID(cloth.ID);

                if(sceneCloth == null)
                {
                    Debug.LogWarning("Expected sceneCloth was null for clothing: " + cloth.ID);
                    continue;
                }

				if (parent == null)
					parent = sceneCloth.transform.parent;

                //move it out of the way so we can properly use parent.childCount when using Destroy instead of DestroyImmediate
                sceneCloth.transform.parent = null;
			
				if (Application.isEditor)
					DestroyImmediate (sceneCloth.gameObject);
				else
					Destroy (sceneCloth.gameObject);
                
                //clean it out of the model
                clothingModel.RemoveItem(cloth);
			}
            DestroyCP(parent);
            parent = null;

            foreach (CIhair hair in content_pack.availableHair)
			{
                if(hair != null)
                {
                    CIhair sceneHair = GetHairByID(hair.ID);
                
                    if (parent == null && sceneHair != null)
                        parent = sceneHair.transform.parent;

                    if (sceneHair != null)
                    {
                        if (sceneHair.transform != null && sceneHair.transform.parent != null)
                        {
                            //move it out of the way so we can properly use parent.childCount when using Destroy instead of DestroyImmediate
                            sceneHair.transform.parent = null;
                        }

                        if (Application.isEditor)
                            DestroyImmediate(sceneHair.gameObject);
                        else
                            Destroy(sceneHair.gameObject);
                    }
				}

                hairModel.RemoveItem(hair);
			}
            DestroyCP(parent);
            parent = null;

			foreach (CIprop prop in content_pack.availableProps)
			{	
				CIprop sceneProp = GetLoadedPropByName(prop.ID);	


				if (parent == null)
					parent = sceneProp.transform.parent;

                //move it out of the way so we can properly use parent.childCount when using Destroy instead of DestroyImmediate
                sceneProp.transform.parent = null;
			
				if (Application.isEditor)
					DestroyImmediate (sceneProp.gameObject);
				else
					Destroy (sceneProp.gameObject);

                propModel.RemoveItem(prop);
			}
            DestroyCP(parent);
            parent = null;

            alphaInjection.invalidate ();
		}


		/// <summary>
		/// Adds a given CIclothing to the overall CostumeModel for this figure. If the CIclothing already exists in the CostumeModel, the GameObject returned is
		/// that of the existant CIclothing.
		/// </summary>
		/// <returns>The property from content pack to figure.</returns>
		/// <param name="prop">The CIclothing to add.</param>
		public GameObject LoadClothingFromContentPackToFigure(CIclothing clothing)
		{
			GameObject clone = null;
			bool has_dupe = false;
		
            //does this clothing item already exist in the clothing model?
			foreach (CostumeItem item in clothingModel.availableItems) {
				if (item.dazName == clothing.dazName) {
                    try
                    {
                        has_dupe = true;
                        clone = item.gameObject; // do we return null or the existing one?
                    }
                    catch
                    {
                        Debug.LogWarning("Attempted to use cached game object that no longer exists");
                    }
                    break;
				}
			}
			
			// we haven't already synced it to the bone service and related services, do so now and clone it
			if (has_dupe == false) {
				CIclothing new_cloth = AttachCIClothing (clothing, true); // should be cloned on heirarchy and bones remapped
				clothingModel.AddItem (new_cloth);
				new_cloth.DetectCoreMeshes ();
                clone = new_cloth.gameObject;
                coreMorphs.Resync(clone.gameObject);
			}

			alphaInjection.invalidate ();
			
			return clone;
		}


		/// <summary>
		/// Adds a given CIhair to the overall CostumeModel for this figure. If the CIhair already exists in the CostumeModel, the GameObject returned is
		/// that of the existant CIhair.
		/// </summary>
		/// <returns>The property from content pack to figure.</returns>
		/// <param name="prop">The CIhair to add.</param>
		public GameObject LoadHairFromContentPackToFigure(CIhair hair)
		{
			GameObject clone = null;
			bool has_dupe = false;

			foreach (CostumeItem item in hairModel.availableItems) {
				if (item.dazName == hair.dazName) {
					has_dupe = true;
					clone = item.gameObject; // do we return null or the existing one?
				}
			}
			
			// instantiate
			if (has_dupe == false) {
				CIhair new_hair = AttachCIHair (hair, true); // should be cloned on heirarchy and bones remapped
				hairModel.AddItem (new_hair);
				new_hair.DetectCoreMeshes (); 	
				clone = new_hair.gameObject;

				CIhair active = (CIhair)hairModel.GetVisibleItems()[0];
                if (active != null)
                {
                    active.SetVisibility(true);
                }
				//					SetVisibilityOnHairItem(active.ID, true);

                coreMorphs.Resync(clone.gameObject);

                //set our variable to point to the clone
                hair = new_hair;
			}

			return clone;
		}


        /// <summary>
        /// Removes the hair cap mask texture
        /// </summary>
        public void ClearHeadOverlay()
        {
            Material material = GetHairMaterial();
            if(material == null)
            {
                return;
            }
            if (material.HasProperty("_Overlay"))
            {
                material.SetTexture("_Overlay", null);
                material.DisableKeyword("_OVERLAY");
            }
        }


        /// <summary>
        /// Places an overlay with the given texture and color on the figure's head.
        /// Overlay for the head replaces the need for a separate skull cap.
        /// </summary>
        /// <param name="texture">Texture for the overlay.</param>
        /// <param name="color">Additive color for the overlay.</param>
        public void InstallOverlay(Texture2D texture,Color color)
        {
            return;
            if(texture == null)
            {
                return;
            }
            Material material = GetHairMaterial();
            if (material.HasProperty("_Overlay"))
            {
                material.SetTexture("_Overlay", texture);
                material.SetColor("_OverlayColor", color);
                material.EnableKeyword("_OVERLAY");
            }
        }


        /// <summary>
        /// The SkinnedMeshRenderers of an MCS Figure each have 3 material slots.
        /// 
        /// Returns the current Material on the Head Material Slot from the current SkinnedMeshRenderer.
        /// </summary>
        /// <returns></returns>
        public Material GetHairMaterial()
        {
            SkinnedMeshRenderer smr = figureMesh.GetSkinnedMeshRenderer();
            Material headMat = figureMesh.GetActiveMaterialInSlot(MATERIAL_SLOT.HEAD);
            return headMat;
        }


        /// <summary>
        /// The SkinnedMeshRenderers of an MCS Figure each have 3 material slots.
        /// 
        /// Returns the current Material on the Body Material Slot from the current SkinnedMeshRenderer.
        /// </summary>
        /// <returns></returns>
        public Material GetBodyMaterial()
        {
            SkinnedMeshRenderer smr = figureMesh.GetSkinnedMeshRenderer();
            Material headMat = figureMesh.GetActiveMaterialInSlot(MATERIAL_SLOT.BODY);
            return headMat;
        }


        /// <summary>
        /// The SkinnedMeshRenderers of an MCS Figure each have 3 material slots.
        /// 
        /// Returns the current Material on teh Eye and Lash Material Slot from the current SkinnedMeshRenderer.
        /// </summary>
        /// <returns></returns>
        public Material GetEyeAndLashMaterial()
        {
            SkinnedMeshRenderer smr = figureMesh.GetSkinnedMeshRenderer();
            Material headMat = figureMesh.GetActiveMaterialInSlot(MATERIAL_SLOT.EYEANDLASH);
            return headMat;
        }


		/// <summary>
		/// Adds a given CIprop to the overall CostumeModel for this figure. If the CIprop already exists in the CostumeModel, the GameObject returned is
		/// that of the existant CIprop.
		/// </summary>
		/// <returns>The property from content pack to figure.</returns>
		/// <param name="prop">The CIprop to add.</param>
		public GameObject LoadPropFromContentPackToFigure(CIprop prop)
		{
			GameObject clone = null;
			bool has_dupe = false;

			foreach (CostumeItem item in propModel.availableItems) {
				if (item.dazName == prop.dazName) {
					has_dupe = true;
					clone = item.gameObject; // do we return null or the existing one?
				}
			}
			
			// instantiate
			if (has_dupe == false) {
				CIprop new_prop = AttachCIProp (prop, true); // should be cloned on heirarchy and bones remapped
				new_prop.DetectCoreMeshes (); 
				propModel.AddItem (new_prop);
				clone = new_prop.gameObject;
			}
			
			return clone;
		}


		/// <summary>
		/// Returns a list of all CIclothings loaded into the character manager.
		/// </summary>
		/// <returns>The all loaded clothing items.</returns>
		public List<CIclothing> GetAllClothing()
		{
			// Debug.Log(clothingModel);
			return clothingModel.GetAllItems ().Cast<CIclothing> ().ToList ();
		}


		/// <summary>
		/// Returns a specific CIclothing loaded into the manager.
		/// </summary>
		/// <returns>The clothing item by name.</returns>
		/// <param name="ID">Display name.</param>
		public CIclothing GetClothingByID(string id)
		{
			return (CIclothing)clothingModel.GetItemByName(id);

		}



//		/// <summary>
//		/// Locks a specific CIclothing by name.
//		/// </summary>
//		/// <param name="clothingItemName">Clothing item name.</param>
//		public void LockClothingItem(string clothingItemName)
//		{
//			clothingModel.SetItemLock(clothingItemName, true);
//		}
//
//		/// <summary>
//		/// Unlocks a specific CIclothing by name.
//		/// </summary>
//		/// <param name="clothingItemName">Clothing item name.</param>
//		public void UnlockClothingItem(string clothingItemName)
//		{
//			clothingModel.SetItemLock(clothingItemName, false);
//		}



		/// <summary>
		/// Returns a list of all CIclothing in the character manager
		/// that are visible.
		/// </summary>
		/// <returns>The visible clothing.</returns>
		public List<CIclothing> GetVisibleClothing()
		{
			return clothingModel.GetVisibleItems ().Cast<CIclothing> ().ToList();
		}


		/// <summary>
		/// Sets a CIclothing items visibility by name.
		/// </summary>
		/// <param name="name">Name.</param>
		/// <param name="visibility">If set to <c>true</c> visibility.</param>
		public void SetClothingVisibility(string id, bool visibility)
		{
			CostumeItem ci = clothingModel.SetItemVisibility (id, visibility);
            coreMorphs.Resync(ci.gameObject);
		}


		/// <summary>
		/// Using DetectAttachedHair(), reloads CIhair
		/// that are currently visible.
		/// </summary>
		public void ReloadHairFromFigure()
		{
			hairModel.ClearItems ();
			DetectAttachedHair ();
		}


		/// <summary>
		/// Returns a list of all CIhair currently
		/// loaded into the character manager.
		/// </summary>
		/// <returns>The all hair items.</returns>
		public List<CIhair> GetAllHair()
		{
			return hairModel.GetAllItems().Cast<CIhair>().ToList();
		}


		/// <summary>
		/// Gets a CIhair (currently loaded into the character manager),
		/// by name.
		/// </summary>
		/// <returns>The hair item by name.</returns>
		/// <param name="name">Name.</param>
		public CIhair GetHairByID(string hair_id)
		{
			return (CIhair)hairModel.GetItemByName (hair_id);
		}


//		public CIhair GetFirstVisibleHair ()
//		{
//			return (CIhair)hairModel.GetVisibleItems ()[0];
//		}


		/// <summary>
		/// Returns a list of all CIhair items that are currently
		/// set to visible.
		/// </summary>
		/// <returns>The all visible hair.</returns>
		public List<CIhair> GetVisibleHair()
		{
			return hairModel.GetVisibleItems().Cast<CIhair>().ToList();
		}



//		/// <summary>
//		/// Locks a specific CIhair item
//		/// </summary>
//		/// <param name="name">Name.</param>
//		public void LockHairItem(string name)
//		{
//			hairModel.SetItemLock(name, true);
//		}
//
//
//
//		public void UnlockHairItem(string name)
//		{
//			hairModel.SetItemLock(name, false);
//		}


		/// <summary>
		/// Sets the visibility on a CIhair item.
		/// </summary>
		/// <param name="name">Name.</param>
		/// <param name="new_vis">If set to <c>true</c> new_vis.</param>
//		public void SetVisibilityOnHair(string name, bool new_vis)
//		{
//			if (hairModel.GetItemByName (name) == null)
//				return;
//			if(new_vis)
//			{
//				CostumeItem[] hairs = hairModel.GetAllItems().ToArray();
//				foreach(CostumeItem hair in hairs)
		//					hairModel.SetItemVisibility (hair.ID, false);
//			}
//			hairModel.SetItemVisibility (name, new_vis);
//		}



		/// <summary>
		/// Returns a list of all CIprops loaded into the character manager.
		/// Note, props attached to attachment points will not be included in
		/// this list (use GetAllAttachedProps() instead).
		/// </summary>
		/// <returns>The all loaded properties.</returns>
		public List<CIprop> GetAllLoadedProps()
		{
			if (propModel.GetAllItems().Count > 0) {
				return propModel.GetAllItems().Cast<CIprop>().ToList();
			}
			return null;
		}


		/// <summary>
		/// Removes a specific prop from all attachment points and
		/// unloads it from the character manager.
		/// </summary>
		/// <param name="prop">Property.</param>
		/// <param name="and_all_instances">If set to <c>true</c> and_all_instances.</param>
		public void DetachAndUnloadProp(CIprop prop, bool and_all_instances = false){
			if (and_all_instances)
			{
				foreach(CIattachmentPoint ap in attachmentPoints)
					DetachPropFromAttachmentPoint(prop.ID, ap.attachmentPointName);

			}
			propModel.RemoveItem(prop);
		}


		/// <summary>
		/// Returns a CIprop item by name, if the item isn't
		/// loaded into the character manager returns a null.
		/// </summary>
		/// <returns>The loaded property by name.</returns>
		/// <param name="name">Name.</param>
		public CIprop GetLoadedPropByName(string name)
		{
			return (CIprop)propModel.GetItemByName(name);
		}


		/// <summary>
		/// Returns a list of all props currently attached
		/// via the attachment point system.  Note, this method
		/// only returns attachment point props, to get a list
		/// of props loaded into the character manager use
		/// GetAllLoadedProps().
		/// </summary>
		/// <returns>The all attached properties.</returns>
		public List<CIprop> GetAllAttachedProps()
		{
			List<CIprop> returnList = new List<CIprop>();
			foreach (CIattachmentPoint ap in attachmentPoints) {
				returnList.AddRange (ap.getAttachmentArray ());
			}
			return returnList;
		}


		/// <summary>
		/// If passed a game object this method adds and 
		/// registers an attachment point.  In order for this
	  	/// method to work correctly the attachment point will
		/// need to be a child of the MCS figures skeleton.
		/// </summary>
		/// <returns>The attachment point from game object.</returns>
		/// <param name="target">Target.</param>
		public CIattachmentPoint CreateAttachmentPointFromGameObject(GameObject target)
		{
			return target.AddComponent<CIattachmentPoint>();
		}


		/// <summary>
		/// Creates and registers an attachment point on
		/// a specific bone in an MCS figure.
		/// </summary>
		/// <returns>The attachment point on bone.</returns>
		/// <param name="bone_name">Bone_name.</param>
		/// <param name="layout">The [Optional] Attachment Point Layout.</param>
		public CIattachmentPoint CreateAttachmentPointOnBone(string bone_name, APLayout layout = null)
		{
			Transform target_bone = boneService.GetBoneByName(bone_name);
			if (bone_name != null) {
				GameObject new_go = new GameObject ();
				new_go.name = target_bone.name + "AttachmentPoint";
				CIattachmentPoint ap = new_go.AddComponent<CIattachmentPoint>();
				if(layout == null)
					layout = new_go.AddComponent<SinglePropDefaultLayout>();
				ap.setLayoutObject(layout);
				ap.transform.SetParent(target_bone);
				ap.transform.localPosition = new Vector3();

				DetectAttachmentPoints();


				DetectAttachmentPoints ();
				return ap;
			}
			return null;
		}


		/// <summary>
		/// Removes a specific attachment point, by name,
		/// from the character manager.  All props attached
		/// to the attachment point will be garbage collected.
		/// </summary>
		/// <param name="name">Name.</param>
		public void DeleteAttachmentPoint(string name)
		{
			CIattachmentPoint point = GetAttachmentPointByName (name);
			if (point == null)
				return;

			string[] props = point.GetAllAttachedPropNames ().ToArray ();
			foreach (string prop in props)
				point.RemoveProp (prop, true);

			if (Application.isEditor)
				DestroyImmediate (point.gameObject);
			else
				Destroy (point.gameObject);
			
			DetectAttachmentPoints ();
		}


		/// <summary>
		/// Returns an array of all CIattachmentPoints on
		/// a given character manager.
		/// </summary>
		/// <returns>The all attachment points.</returns>
		public CIattachmentPoint[] GetAllAttachmentPoints()
		{
			DetectAttachmentPoints();
			return attachmentPoints.ToArray();
		}


		/// <summary>
		/// Returns a specific CIattachementPoint when requested
		/// by name.  Null if the attachment point doesn't exist.
		/// </summary>
		/// <returns>The attachment point by name.</returns>
		/// <param name="name">Name.</param>
		public CIattachmentPoint GetAttachmentPointByName(string name)
		{
			foreach(CIattachmentPoint at in attachmentPoints) {
				if (at.attachmentPointName == name)
					return at;
			}
			return null;
		}


		/// <summary>
		/// Attachs a CIprop, by name, to an attachment point,
		/// also by name.
		/// </summary>
		/// <param name="propName">Property name.</param>
		/// <param name="attachmentPointName">Attachment point name.</param>
		public void AttachPropToAttachmentPoint(string propName, string attachmentPointName)
		{
			CIprop prop = GetLoadedPropByName(propName);
			if (prop == null)
				return;
			
			CIattachmentPoint point = GetAttachmentPointByName(attachmentPointName);
			if (point == null)
				return;

			point.AddProp(prop, true);
		}


		/// <summary>
		/// Removes CIprop, by name, from a specific attachment point, also by name.
		/// </summary>
		/// <param name="propName">Property name.</param>
		/// <param name="attachmentPointName">Attachment point name.</param>
		public void DetachPropFromAttachmentPoint(string propName, string attachmentPointName)
		{
			CIattachmentPoint point = GetAttachmentPointByName(attachmentPointName);
			if (point == null)
				return;
			
			CIprop prop = point.GetPropByName(propName);
			if (prop == null)
				return;
			
			point.RemoveProp(prop, true);
		}


		/// <summary>
		/// Returns a bone from an MCS figure's skeleton.
        /// 
        /// If the bone does not exist in the skeleton returns null.
        /// GetAllBoneNames can be used to verify the existance of a bone name in the skeleton.
		/// </summary>
		/// <returns>The bone by name.</returns>
		/// <param name="name">Name.</param>
		public Transform GetBoneByName(string name)
		{
			return boneService.GetBoneByName(name);
		}

		/// <summary>
		/// Returns a string array with all bone names contained
		/// in the MCS figure's skeleton.
		/// </summary>
		/// <returns>The all bones names.</returns>
		public string[] GetAllBonesNames()
		{
			return boneService.getAllBonesNames();
		}
        

        /// <summary>
        /// Empties the MaterialBuffer in the AlphaInjectionManager
        /// </summary>
        /// <param name="fetch"></param>
        /// <remarks>
        /// It doesn't appear that ClearMaterialBuffers does anything anymore.
        /// Is this just here for legacy code? If so we should deprecate it.
        /// </remarks>
        public void ClearAlphaInjectionBuffers(bool fetch=true)
        {
            alphaInjection.ClearMaterialBuffers(fetch);
        }


        /*
        public void ClearAlphaInjectionCloneMaterials()
        {
            alphaInjection.FreeMaterials();
        }
        public void RestoreAlphaInjectionMaterials()
        {
            alphaInjection.RestoreMaterials();
        }
        */


        /// <summary>
        /// Marks the current AlphaInjectionManager as dirty so that it will be 
        /// re-rendered the next time it is processed.
        /// </summary>
        public void SyncAlphaInjection()
        {
            alphaInjection.invalidate();
        }


        /// <summary>
        /// Called by the Unity Engine when the GameObject is destroyed.
        /// </summary>
        public void OnDestroy()
        {
            if(alphaInjection != null)
            {
                alphaInjection.cleanup();
            }
            if(coreMorphs != null)
            {
                coreMorphs.OnPostMorph -= OnPostMorph;
            }
            if(jct != null)
            {
                jct.OnPostJCT -= OnPostJCT;
            }
        }


        /// <summary>
        /// Attempts to find items that are on the figure that should not be and will remove them
        /// </summary>
        public void RemoveRogueContent()
        {
            contentPackModel.RemoveRogueContent(gameObject);
        }


        /// <summary>
        /// Forces an update on the current bounding boxes for SkinnedMeshRenderers.
        /// 
        /// See also <see cref = "CostumeBoundsUpdateFrequency" />
        /// </summary>
        public void ResyncBounds()
        {
            CostumeItem[] costumeItems = GetComponentsInChildren<CostumeItem>();
            foreach (CostumeItem ci in costumeItems) 
            {
                if (!ci.enabled)
                {
                    continue;
                }
                ci.RecalculateBounds();
            }
        }


        /// <summary>
        /// Save blendshape data into an XML file for later use
        /// </summary>
        /// <param name="location"></param>
        public void SaveBlendshapeData(string location) {

            Debug.Log("Saving to: " + location);

            //TODO: Allow for canceling the overwrite of files

            //SkinnedMeshRenderer smr = _figureMesh.GetSkinnedMeshRenderer(); //get the skinned mesh renderer for this figure
            string fHeader = "Blendshape Character Data";               //header will be added to all saved blendshape files
            List<BlendData> shapes = new List<BlendData>();
            BlendData bData;
            XmlSerializer serializer = new XmlSerializer(typeof(List<BlendData>));
            //int counter = 0;                                            //set our starting counter
            
            bData.blendName = fHeader;                                  //make the first
            bData.check = true;                                         //more for future use, but true means this is a good file
            bData.blendValue = 0.0f;                                    //making the value nothing since it doesn't matter
            shapes.Add(bData);
            //counter++; 

            for (int i = 0; i < coreMorphs.morphs.Count; i++) {

                //check to see if morph has been adjusted
                if ((coreMorphs.morphs[i].attached) || (coreMorphs.morphs[i].localName.Contains("eCTRL"))) {

                    bData.blendName = coreMorphs.morphs[i].localName;
                    bData.check = false;
                    bData.blendValue = coreMorphs.morphs[i].value;
                    shapes.Add(bData);
                    //counter++;

                }

            }

            FileStream stream = new FileStream(location, FileMode.Create);
            serializer.Serialize(stream, shapes);
            stream.Close();

            Debug.Log("Blendshape data file saved.");

            //smr.b
        }

        /// <summary>
        /// Loads blendshapes from a file and applies them to the model.
        /// </summary>
        /// <param name="location"></param>
        public void LoadBlendshapeData(string location) {

            Debug.Log("Loading file from: " + location);
            XmlSerializer serializer = new XmlSerializer(typeof(List<BlendData>));  //set up our serializer
            FileStream stream = new FileStream(location, FileMode.Open, FileAccess.Read);    //set up our data stream for loading the file
            List<BlendData> shapes = new List<BlendData>();                 //create the holder of our data
            shapes = serializer.Deserialize(stream) as List<BlendData>;     //load our data into the list

            //verify this data is correct
            if ((shapes[0].blendName != "Blendshape Character Data") && !(shapes[0].check)) {

                Debug.Log("Unable to load file.  Data is corrupt or saved improperly.");
                return;

            }

            //everything looks good so lets update the blendshapes
            for (int i = 1; i < shapes.Count; i++) {

                SetBlendshapeValue(shapes[i].blendName, shapes[i].blendValue);

            }

            stream.Close();
            Debug.Log("Blendshapes loaded and set properly.");

        }


        public void UpdateJCTMorphs() {

            XmlSerializer serializer = new XmlSerializer(typeof(JCTMorphTrans[]));
            JCTTransition jTransition = GetComponentInChildren<JCTTransition>();        //get the CoreMesh script
            JCTMorphTrans[] file = new JCTMorphTrans[jTransition.m_morphs.Length];
            String fileLoc;                                             //temp string for file location

            //no need to update again once the transition has been updated once
            if (jTransition.jctUpdateFlag)
                return;

            //check to see if we're dealing with a male of female model
            if (_figureMesh.dazName == "MCSFemale")
                fileLoc = "/MCS/Code/Plugins/MCSFemale2017.core";
            else
                fileLoc = "/MCS/Code/Plugins/MCSMale2017.core";

            FileStream stream = new FileStream(Application.dataPath + fileLoc, FileMode.Open, FileAccess.Read);
            file = serializer.Deserialize(stream) as JCTMorphTrans[];
            stream.Close();

            //gotta put the data back into the JCTTransition
            for (int i = 0; i < jTransition.m_morphs.Length; i++) {

                jTransition.m_morphs[i].m_name = file[i].m_name;        //store the name of the morph
                jTransition.m_morphs[i].m_value = file[i].m_value;      //store the value
                jTransition.m_morphs[i].m_target = file[i].m_target;    //store the target
                jTransition.m_morphs[i].m_nodes = file[i].m_nodes;      //copy over the nodes value
                jTransition.m_morphs[i].m_offsets = file[i].m_offsets;  //copy over the offset data

            }

            jTransition.jctUpdateFlag = true;                           //make a note that we updated the JCT stuff
            EditorUtility.SetDirty(this);                               //set it up so that everything is updated properly and saved


        }

                                                                        //indent marker
        public void ExportJCTMorphs() {

            XmlSerializer serializer = new XmlSerializer(typeof(JCTMorphTrans[]));
            JCTTransition jTransition = GetComponentInChildren<JCTTransition>();        //get the CoreMesh script
            JCTMorphTrans[] file = new JCTMorphTrans[jTransition.m_morphs.Length];

            //need to fix the texture constructor issue
            for (int i = 0; i < jTransition.m_morphs.Length; i++) {

                file[i].m_name = jTransition.m_morphs[i].m_name;
                file[i].m_value = jTransition.m_morphs[i].m_value;
                file[i].m_target = jTransition.m_morphs[i].m_target;
                file[i].m_nodes = jTransition.m_morphs[i].m_nodes;
                file[i].m_offsets = jTransition.m_morphs[i].m_offsets;

                if (jTransition.m_morphs[i].m_image != null)
                    file[i].m_image_loc = jTransition.m_morphs[i].m_image.name;
                else
                    file[i].m_image_loc = "";

            }

            String fileLoc;                                             //temp variable for file location

            //verify if this is a female or male model
            if (_figureMesh.dazName == "MCSFemale")
                fileLoc = "/MCS/Code/Plugins/MCSFemale2017.core";
            else
                fileLoc = "/MCS/Code/Plugins/MCSMale2017.core";

            FileStream stream = new FileStream(Application.dataPath + fileLoc, FileMode.Create);
            serializer.Serialize(stream, file);
            stream.Close();

        }

        /// <summary>
        /// Get the vertex data and put it into an XML file.  Requires the file location
        /// of the streamed assets file.
        /// </summary>
        /// <param name="file"></param>
        public void ExportVertexData(List<CoreMesh> myMeshes) {

            //check to see if this is the male model
            //if (_figureMesh.dazName == "MCSMale")
            //    file = "MCSMale/MCSMale/MCSMale_LOD0";
            
            foreach (CoreMesh cm in myMeshes) {

                Vector3[] verts;
                SkinnedMeshRenderer smr = cm.skinnedMeshRenderer;       //get the skinned mesh renderer for this object
                XmlSerializer serializer = new XmlSerializer(typeof(Vector3[]));
                
                //Debug.Log("Morph Path: " + cm.runtimeMorphPath);
                //Debug.Log("Streaming Assets: " + streamBase);
                //Debug.Log("Loc: " + Application.dataPath + streamBase + cm.runtimeMorphPath + ".covx");
                FileStream stream = new FileStream(Application.dataPath + streamBase + cm.runtimeMorphPath + ".covx", FileMode.Create);
               
                verts = smr.sharedMesh.vertices;                        //save the vertices for this mesh
                serializer.Serialize(stream, verts);                    //save the verts
                stream.Close();                                         //vertices extracted
                
                Debug.Log("Extracted: " + cm.runtimeMorphPath + " ::");

            }


            Debug.Log("Extraction complete.");

        }

        /// <summary>
        /// Used for converting vertices for meshes when using Unity 2018 and newer.  The basis of 
        /// this conversion was done by @ru_erikvdb.  He deserves credit for doing a lot of the
        /// leg work.
        /// </summary>
        /// <param name="myMeshes"></param>
        public void ConvertVertexData(List<CoreMesh> myMeshes) {

            Debug.Log("Converting model using 2017 vertices....");
            int mCounter = 0;                                           //counter for how many meshes in this set has been updated

            //seems redundant, but to avoid excessive loading, we're gonna check fies before we load in asset paths
            //checking before we do anything serious will cut down on loading and beach ball time
            foreach (CoreMesh cm in myMeshes) {

                string file = Application.dataPath + streamBase + cm.runtimeMorphPath + ".covx";    //build the location for the .covx

                //we need to see if even one of the meshes in this set hasn't been updated
                if (!File.Exists(file))
                    break;                                              //get out of this loop because one of the files hasn't been updated
                else if (File.ReadAllText(file).Contains("!--Unity 2018+ Compatible--!"))
                     mCounter++;

            }

            //see if all of the meshes have been updated
            if (mCounter == myMeshes.Count)
                return;

            ProjectionMeshMap pmm = new ProjectionMeshMap();
            StreamingMorphs sm = new StreamingMorphs();
            StreamingMorphs.LoadMainThreadAssetPaths();
            XmlSerializer serializer = new XmlSerializer(typeof(Vector3[]));    //set up our serializer so that we can read the vertex files

            var manifest = sm.GetManifest(name);

            Debug.Log("Looking over core meshes....");
            //cycle through all of the CoreMeshes on this model
            foreach (CoreMesh cm in myMeshes) {

                Vector3[] verts;                                        //varible to hold our vertices loaded from the file
                SkinnedMeshRenderer smr = cm.skinnedMeshRenderer;       //get the skinned mesh renderer for this object
                string file = Application.dataPath + streamBase + cm.runtimeMorphPath + ".covx";    //set up the string for the file to grab
                string streamFile = Path.Combine(Application.streamingAssetsPath, cm.runtimeMorphPath);  //get the morph streaming assets location
                string tempFile = Application.temporaryCachePath + "/temp"; //temp location for storing files

                //check to see if this file exists
                if (!File.Exists(file)) {

                    Debug.Log("The vertex file for " + cm.name + " doesn't exist at location " + file + ".");
                    continue;

                }

                //need to find out if this model has been updated yet
                if (File.ReadAllText(file).Contains("!--Unity 2018+ Compatible--!")) {
                    
                    Debug.Log("This mesh (" + smr.name + ") has already been updated.");   //make a note in the Editor that this mesh has been updated already
                    continue;

                }

                Debug.Log("We're continuing now.");

                FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read);    //set up our file stream to grab the data
                verts = serializer.Deserialize(stream) as Vector3[];        //grab our verts
                stream.Close();                                             //close stream (don't forgot to do this)

                //string morphPath = Application.temporaryCachePath + "/temp";    //make a temporary folder for putting files
                Directory.CreateDirectory(tempFile);                        //create the temporary directory for storing morph files

                Dictionary<int, int> tsm = pmm.GenerateTargetToSourceMap(verts, smr.sharedMesh.vertices);   //want to compare verts between the older verts and the
                                                                            //current verts on the mesh

                //Process morphs now
                //int count = 0;
                //int total = manifest.names.Length;                          //get number of items in the array
                List<string> morphNames = new List<string>(manifest.names); //make new list to hold list of morph names will allow for reordering and the like
                morphNames.Add("base");                                     //needed for clothing and hair morphs and how they connect to JCT lists

                Debug.Log("Starting to compare morphs....");

                //now we need to look over each morph and compare
                foreach (string mName in morphNames) {

                    MorphData sourceMD = sm.GetMorphDataFromResources(streamFile, mName);   //get the data for the selected morph from the streaming assets file

                    //see if the morph data is actually there
                    if (sourceMD != null) {

                        MorphData targetMD = RemapMorphData(smr, sourceMD, tsm);    //reorder verts to match old versions of Unity
                        MCS_Utilities.MorphExtraction.MorphExtraction.WriteMorphDataToFile(targetMD, tempFile + "/" + targetMD.name + ".morph", false, false);

                    }
                    
                }

                File.AppendAllText(file, "!--Unity 2018+ Compatible--!");   //add this when needed to signal this file has been converted

                //generate .morphs.mr file again
                string tmpFile = tempFile.Replace(@"\", @"/");
                string newFile = streamFile + ".morphs.mr";                   //append morph.mr to name to make the correct .mr file
                MCS_Utilities.MorphExtraction.MorphExtraction.MergeMorphsIntoMR(tmpFile, newFile);

                //We're done!!
                MCS_Utilities.Paths.TryDirectoryDelete(tempFile);           //delete the directory we were using to build morphs

            }

            Debug.Log("Conversion complete.");
            Debug.LogWarning("Please RESTART Unity for changes to take effect.");

        }


        /// <summary>
        /// Remaps the morph data and is taken from MCS SteamingMorphs built-in ConvertMorphDataFromMap,
        /// but without the requirement for a projectionmap.  This function was contributed by
        /// @ru_erikvdb.
        /// </summary>
        /// <param name="smr"></param>
        /// <param name="morphData"></param>
        /// <param name="tsMap"></param>
        /// <returns></returns>
        MorphData RemapMorphData(SkinnedMeshRenderer smr, MorphData morphData, Dictionary<int, int> tsMap) {

            Mesh mesh = smr.sharedMesh;
            Vector3[] targetVertices = mesh.vertices;

            MorphData morphDataNew = new MorphData();
            morphDataNew.name = morphData.name;
            morphDataNew.jctData = morphData.jctData;
            morphDataNew.blendshapeData = new BlendshapeData();
            morphDataNew.blendshapeData.frameIndex = morphData.blendshapeData.frameIndex;
            morphDataNew.blendshapeData.shapeIndex = morphData.blendshapeData.shapeIndex;

            morphDataNew.blendshapeData.deltaVertices = new Vector3[targetVertices.Length];
            morphDataNew.blendshapeData.deltaNormals = new Vector3[targetVertices.Length];
            morphDataNew.blendshapeData.deltaTangents = new Vector3[targetVertices.Length];
            
            foreach (var ts in tsMap) {
                if (morphData.blendshapeData.deltaNormals != null) {
                    morphDataNew.blendshapeData.deltaNormals[ts.Key] = morphData.blendshapeData.deltaNormals[ts.Value];
                }
                if (morphData.blendshapeData.deltaVertices != null) {
                    if (ts.Key >= morphDataNew.blendshapeData.deltaVertices.Length) {
                        throw new System.Exception("ts.key in: " + smr.name + " is too large for deltas: " + ts.Key + " => " + ts.Value + " | " + morphDataNew.blendshapeData.deltaVertices.Length);
                    }
                    if (ts.Value >= morphData.blendshapeData.deltaVertices.Length) {
                        throw new System.Exception("ts.value in: " + smr.name + " is too large for deltas: " + ts.Key + " => " + ts.Value + " | " + morphData.blendshapeData.deltaVertices.Length);
                    }
                    morphDataNew.blendshapeData.deltaVertices[ts.Key] = morphData.blendshapeData.deltaVertices[ts.Value];
                }
                if (morphData.blendshapeData.deltaTangents != null) {
                    morphDataNew.blendshapeData.deltaTangents[ts.Key] = morphData.blendshapeData.deltaTangents[ts.Value];
                }
            }

            return morphDataNew;
           
        }
        
    }
}
