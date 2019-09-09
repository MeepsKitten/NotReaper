﻿using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using Michsky.UI.ModernUIPack;
using NotReaper.Grid;
using NotReaper.IO;
using NotReaper.Managers;
using NotReaper.Models;
using NotReaper.Targets;
using NotReaper.Tools;
using NotReaper.UI;
using NotReaper.UserInput;
using SFB;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace NotReaper {


	public class Timeline : MonoBehaviour {

		public static AudicaFile audicaFile;

		public bool SustainParticles = true;
		public DropdownToVelocity CurrentSound;
		public static Timeline TimelineStatic;
		public float playbackSpeed = 1f;

		[SerializeField] private Renderer timelineBG;

		public static Transform gridNotesStatic;
		public static Transform timelineNotesStatic;

		[SerializeField] private Transform timelineTransformParent;
		[SerializeField] private Transform gridTransformParent;

		[SerializeField] private AudioSource aud;
		[SerializeField] private AudioSource previewAud;
		[SerializeField] private AudioSource leftSustainAud;
		[SerializeField] private AudioSource rightSustainAud;
		[SerializeField] private Transform spectrogram;


		[SerializeField] private TextMeshProUGUI songTimestamp;
		[SerializeField] private TextMeshProUGUI curTick;
		[SerializeField] private Slider bottomTimelineSlider;

		[SerializeField] private HorizontalSelector beatSnapSelector;
		private UnityAction SetSnapWithSliderAction;

		private Color leftColor;
		private Color rightColor;
		private Color bothColor;
		private Color neitherColor;

		public float sustainVolume;

		public List<Target> notes;
		public static List<Target> orderedNotes;

		//Contains the notes that the player can actually see.
		public static List<Target> selectableNotes;

		public static List<Target> loadedNotes;
		//List<TargetIcon> notesTimeline;


		public TargetIcon timelineTargetIconPrefab;
		public TargetIcon gridTargetIconPrefab;


		public float previewDuration = 0.1f;

		public static float time { get; private set; }

		private int beatSnap = 4;
		private int scale = 20;
		private float targetScale = 0.7f;
		private float scaleOffset = 0;
		private static float bpm = 60;
		private static int offset = 0;
		// .Desc menu
		// private string songid = "";
		// private string songtitle = "";
		// private string songartist = "";
		// private string songendevent = "";
		// private float songpreroll = 0.0f;
		// private string songauthor = "";
		// private string mogg = "";

		private int seconds;
		public bool songLoaded = false;

		private TargetHandType selectedHandType = TargetHandType.Right;
		private TargetBehavior selectedBehaviour = TargetBehavior.Standard;
		private TargetVelocity selectedVelocity = TargetVelocity.Standard;

		private float timelineMaterialOffset;

		private bool hover = false;
		private bool paused = true;
		public bool projectStarted = false;

		//private DirectorySecurity securityRules = new DirectorySecurity();

		public Metronome metro;


		//Tools
		[SerializeField] public EditorToolkit Tools;

		[SerializeField] private MiniTimeline miniTimeline;

		private void Start() {

			//Load the config file
			NRSettings.LoadSettingsJson();


			notes = new List<Target>();
			orderedNotes = new List<Target>();
			//notesTimeline = new List<TimelineTarget>();
			selectableNotes = new List<Target>();
			loadedNotes = new List<Target>();

			//securityRules.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.FullControl, AccessControlType.Allow));

			gridNotesStatic = gridTransformParent;
			timelineNotesStatic = timelineTransformParent;
			TimelineStatic = this;

			//Modify the note colors
			leftColor = NRSettings.config.leftColor;
			rightColor = NRSettings.config.rightColor;
			bothColor = UserPrefsManager.bothColor;
			neitherColor = UserPrefsManager.neitherColor;


			StartCoroutine(CalculateNoteCollidersEnabled());


		}

		void OnApplicationQuit() {
			//DirectoryInfo dir = new DirectoryInfo(Application.persistentDataPath + "\\temp\\");
			//dir.Delete(true);
		}

		//When loading from cues, use this.
		public void AddTarget(Cue cue) {
			Vector2 pos = NotePosCalc.PitchToPos(cue);

			if (cue.tickLength / 480 >= 1)
				//AddTarget(pos.x, pos.y, (cue.tick - offset) / 480f, cue.tickLength, cue.velocity, cue.handType, cue.behavior, false);
				AddTarget(pos.x, pos.y, (cue.tick - offset) / 480f, false, false, cue.tickLength, cue.velocity, cue.handType, cue.behavior);
			else
				AddTarget(pos.x, pos.y, (cue.tick - offset) / 480f, false, false, cue.tickLength, cue.velocity, cue.handType, cue.behavior);
		}


		//Use when adding a singular target to the project (from the user)
		public void AddTarget(float x, float y) {
			AddTarget(x, y, BeatTime(), true, true);
		}

		//Use for adding a target from redo/undo
		public void AddTarget(Target target, bool genUndoAction = true) {
			AddTarget(target.gridTargetPos.x, target.gridTargetPos.y, target.gridTargetPos.z, false, genUndoAction, target.beatLength, target.velocity, target.handType, target.behavior);
		}

		//When adding many notes at once from things such as copy paste.
		public void AddTargets(List<Target> targets, bool genUndoAction = true) {

			//We need to generate a custom undo action (if requested), so we set the default undo generation to false.

			foreach (Target target in targets) {
				AddTarget(target.gridTargetPos.x, target.gridTargetPos.y, target.gridTargetPos.z, false, false, target.beatLength, target.velocity, target.handType, target.behavior);
			}

			if (genUndoAction) {
				NRAction action = new NRAction();
				action.type = ActionType.MultiAddNote;
				action.affectedTargets = targets;
				Tools.undoRedoManager.AddAction(action, true);
			}

		}


		/// <summary>
		/// Adds a target to the timeline.
		/// </summary>
		/// <param name="x">x pos</param>
		/// <param name="y">y pos</param>
		/// <param name="beatTime">Beat time to add the note to</param>
		/// <param name="userAdded">If true, fetch values for currently selected note type from EditorInput, if not, use values provided in function.</param>
		/// <param name="beatLength"></param>
		/// <param name="velocity"></param>
		/// <param name="handType"></param>
		/// <param name="behavior"></param>
		public void AddTarget(float x, float y, float beatTime, bool userAdded = true, bool genUndoAction = true, float beatLength = 0.25f, TargetVelocity velocity = TargetVelocity.Standard, TargetHandType handType = TargetHandType.Left, TargetBehavior behavior = TargetBehavior.Standard) {


			float yOffset = 0;
			float zOffset = 0;

			//Calculate the note offset for visual purpose on the timeline.
			if (handType == TargetHandType.Left) {
				yOffset = 0.1f;
				zOffset = 0.1f;
			} else if (handType == TargetHandType.Right) {
				yOffset = -0.1f;
				zOffset = 0.2f;
			}

			Target target = new Target();

			target.timelineTargetIcon = Instantiate(timelineTargetIconPrefab, timelineTransformParent);
			target.timelineTargetIcon.transform.localPosition = new Vector3(beatTime, yOffset, zOffset);

			target.gridTargetIcon = Instantiate(gridTargetIconPrefab, gridTransformParent);
			target.gridTargetIcon.transform.localPosition = new Vector3(x, y, beatTime);


			//Use when the rest of the inputs aren't supplied, get them from the EditorInput script.
			if (userAdded) {

				target.SetHandType(EditorInput.selectedHand);

				target.SetBehavior(EditorInput.selectedBehavior);

				switch (CurrentSound) {
					case DropdownToVelocity.Standard:
						target.SetVelocity(TargetVelocity.Standard);
						break;

					case DropdownToVelocity.Snare:
						target.SetVelocity(TargetVelocity.Snare);
						break;

					case DropdownToVelocity.Percussion:
						target.SetVelocity(TargetVelocity.Percussion);
						break;

					case DropdownToVelocity.ChainStart:
						target.SetVelocity(TargetVelocity.ChainStart);
						break;

					case DropdownToVelocity.Chain:
						target.SetVelocity(TargetVelocity.Chain);
						break;

					case DropdownToVelocity.Melee:
						target.SetVelocity(TargetVelocity.Melee);
						break;

					default:
						target.SetVelocity(TargetVelocity.Standard);
						break;

				}
			}

			//If userAdded false, we use the supplied inputs to generate the note.
			else {

				target.SetHandType(handType);

				target.SetBehavior(behavior);

				target.SetVelocity(velocity);
			}


			if (target.behavior == TargetBehavior.Hold) {
				target.SetBeatLength(beatLength);
			} else {
				target.SetBeatLength(0.25f);
			}

			//target.gridTargetIcon.GetComponentInChildren<Canvas>().worldCamera = Camera.main;

			//Now that all inital dependencies are met, we can init the target. (Loads sustain controller)
			target.Init();

			notes.Add(target);

			orderedNotes = notes.OrderBy(v => v.gridTargetIcon.transform.position.z).ToList();


			//Subscribe to the delete note event so we can delete it if the user wants. And other events.
			target.DeleteNoteEvent += DeleteTarget;

			target.TargetEnterLoadedNotesEvent += AddLoadedNote;
			target.TargetExitLoadedNotesEvent += RemoveLoadedNote;

			target.MakeTimelineUpdateSustainLengthEvent += UpdateSustainLength;


			if (genUndoAction) {
				//Add to undo redo manager if requested.
				NRAction action = new NRAction();
				action.type = ActionType.AddNote;
				action.affectedTargets.Add(notes.Last());

				//If the user added the note, we clear the redo actions. If the manager added it, we don't clear redo actions.
				Tools.undoRedoManager.AddAction(action, userAdded);

			}

			//We have to load the mini timeline after the audio clips load.
			//miniTimeline.AddNote(beatTime / aud.clip.length, handType);

			//UpdateTrail();
			//UpdateChainConnectors();

		}


		private void UpdateSustains() {


			foreach (var note in loadedNotes) {
				if (note.behavior == TargetBehavior.Hold) {
					if ((note.gridTargetIcon.transform.position.z < 0) && (note.gridTargetIcon.transform.position.z + note.beatLength / 480f > 0)) {

						var particles = note.gridTargetIcon.GetComponentInChildren<ParticleSystem>();
						if (!particles.isEmitting) {
							particles.Play();

							float panPos = (float) (note.gridTargetIcon.transform.position.x / 7.15);
							if (note.handType == TargetHandType.Left) {
								leftSustainAud.volume = sustainVolume;
								leftSustainAud.panStereo = panPos;

							} else if (note.handType == TargetHandType.Right) {
								rightSustainAud.volume = sustainVolume;
								rightSustainAud.panStereo = panPos;
							}


							var main = particles.main;
							main.startColor = note.handType == TargetHandType.Left ? new Color(leftColor.r, leftColor.g, leftColor.b, 1) : new Color(rightColor.r, rightColor.g, rightColor.b, 1);
						}

						ParticleSystem.Particle[] parts = new ParticleSystem.Particle[particles.particleCount];
						particles.GetParticles(parts);

						for (int i = 0; i < particles.particleCount; ++i) {
							parts[i].position = new Vector3(parts[i].position.x, parts[i].position.y, 0);
						}

						particles.SetParticles(parts, particles.particleCount);

					} else {
						var particles = note.gridTargetIcon.GetComponentInChildren<ParticleSystem>();
						if (particles.isEmitting) {
							particles.Stop();
							if (note.handType == TargetHandType.Left) {
								leftSustainAud.volume = 0.0f;
							} else if (note.handType == TargetHandType.Right) {
								rightSustainAud.volume = 0.0f;
							}
						}
					}
				}
			}

		}

		private void UpdateChords() {
			/*
			//TODO: ORDERED
			if (loadedNotes.Count > 0)
				foreach (var note in loadedNotes) {
					if (note && (Mathf.Round(note.transform.position.z) == 0 && note.behavior != TargetBehavior.ChainStart && note.behavior != TargetBehavior.Chain && note.behavior != TargetBehavior.HoldEnd)) {
						LineRenderer lr = note.GetComponent<LineRenderer>();

						List<Material> mats = new List<Material>();
						lr.GetMaterials(mats);
						Color activeColor = (note.handType == TargetHandType.Left) ? leftColor : rightColor;
						mats[0].SetColor("_EmissionColor", new Color(activeColor.r, activeColor.g, activeColor.b, activeColor.a / 2));

						var positionList = new List<Vector3>();
						lr.SetPositions(positionList.ToArray());
						lr.positionCount = 0;
						//TODO: Ordered
						foreach (var note2 in loadedNotes) {
							if (note.transform.position.z == note2.transform.position.z && note != note2) {
								var positionList2 = new List<Vector3>();

								var offset = (note.handType == TargetHandType.Left) ? 0.01f : -0.01f;
								positionList2.Add(note.transform.position + new Vector3(0, offset, 0));
								positionList2.Add(note2.transform.position + new Vector3(0, offset, 0));
								lr.positionCount = 2;

								positionList2.Remove(new Vector3(0, 0, 1));

								lr.SetPositions(positionList2.ToArray());

							}

						}

					} else {
						LineRenderer lr = note.GetComponent<LineRenderer>();

						if (lr.positionCount != 0) {
							var positionList = new List<Vector3>();
							lr.SetPositions(positionList.ToArray());
							lr.positionCount = 0;
						}
					}
				}
			*/
		}

		private void UpdateChainConnectors() {

			/*
			if (orderedNotes.Count > 0)
				foreach (var note in orderedNotes) {
					if (note.behavior == TargetBehavior.ChainStart) {
						LineRenderer lr = note.GetComponentsInChildren<LineRenderer>() [1];

						List<Material> mats = new List<Material>();
						lr.GetMaterials(mats);
						Color activeColor = note.handType == TargetHandType.Left ? leftColor : rightColor;
						mats[0].SetColor("_Color", activeColor);
						mats[0].SetColor("_EmissionColor", activeColor);

						//clear pos list
						lr.SetPositions(new Vector3[2]);
						lr.positionCount = 2;

						//set start pos
						lr.SetPosition(0, note.transform.position);
						lr.SetPosition(1, note.transform.position);

						//add links in chain
						List<GridTarget> chainLinks = new List<GridTarget>();
						foreach (var note2 in orderedNotes) {
							//if new start end old chain
							if ((note.handType == note2.handType) && (note2.transform.position.z > note.transform.position.z) && (note2.behavior == TargetBehavior.ChainStart) && (note2 != note))
								break;

							if ((note.handType == note2.handType) && note2.transform.position.z > note.transform.position.z && note2.behavior == TargetBehavior.Chain) {
								chainLinks.Add(note2);
							}

						}

						note.chainedNotes = chainLinks;

						//aply lines to chain links
						if (chainLinks.Count > 0) {
							var positionList = new List<Vector3>();
							positionList.Add(new Vector3(note.transform.position.x, note.transform.position.y, 0));
							foreach (var link in chainLinks) {
								//add new
								if (!positionList.Contains(link.transform.position)) {
									positionList.Add(new Vector3(link.transform.position.x, link.transform.position.y, link.transform.position.z - note.transform.position.z));
								}
							}
							lr.positionCount = positionList.Count;
							positionList = positionList.OrderByDescending(v => v.z - note.transform.position.z).ToList();

							for (int i = 0; i < positionList.Count; ++i) {
								positionList[i] = new Vector3(positionList[i].x, positionList[i].y, 0);
							}

							var finalPositions = positionList.ToArray();
							lr.SetPositions(finalPositions);
						}
					}
				}
				*/

		}


		public static void AddSelectableNote(Target target) {
			selectableNotes.Add(target);
		}

		public static void RemoveSelectableNote(Target target) {
			selectableNotes.Remove(target);
		}

		public static void AddLoadedNote(Target target) {
			loadedNotes.Add(target);
		}

		public static void RemoveLoadedNote(Target target) {
			loadedNotes.Remove(target);
		}

		/// <summary>
		/// Updates a sustain length from the buttons next to sustains.
		/// </summary>
		/// <param name="target">The target to affect</param>
		/// <param name="increase">If true, increase by one beat snap, if false, the opposite.</param>
		public void UpdateSustainLength(Target target, bool increase) {
			if (target.behavior != TargetBehavior.Hold) return;

			if (increase) {
				target.UpdateSustainBeatLength(target.beatLength += (480 / beatSnap) * 4f);
			} else {
				target.UpdateSustainBeatLength(Mathf.Max(target.beatLength -= (480 / beatSnap) * 4f, 0));
			}
		}

		//public void DeleteTarget(Target target, bool genUndoAction) {
		//if (target.gridTargetIcon != null) {
		//TODO: Add undo back later
		//target.gridTarget.oldRedoPosition = target.gridTarget.transform.localPosition;
		//}
		//DeleteTarget(target);

		// if (genUndoAction) {
		//    Action action = new Action();
		//    action.affectedTargets.Add(target.gridTarget);
		//    action.type = ActionType.RemoveNote;
		//    Tools.undoRedoManager.AddAction(action);
		// }

		//}


		public void DeleteTarget(Target target, bool genUndoAction = true, bool clearRedoActions = true) {

			if (target == null) return;

			if (genUndoAction) {
				NRAction action = new NRAction();
				action.affectedTargets.Add(target);
				action.type = ActionType.RemoveNote;

				Tools.undoRedoManager.AddAction(action, clearRedoActions);
			}

			notes.Remove(target);
			orderedNotes.Remove(target);
			selectableNotes.Remove(target);
			loadedNotes.Remove(target);

			if (target.gridTargetIcon)
				Destroy(target.gridTargetIcon.gameObject);

			if (target.timelineTargetIcon)
				Destroy(target.timelineTargetIcon.gameObject);

			target = null;


			//UpdateChainConnectors();
			//UpdateChords();

		}

		public void DeleteTargets(List<Target> targets, bool genUndoAction = true, bool clearRedoActions = true) {

			//We need to add a custom undo action, so we skip the default one.
			foreach (Target target in targets) {
				DeleteTarget(target, false);
			}

			if (genUndoAction) {
				NRAction action = new NRAction();
				action.type = ActionType.MultiRemoveNote;
				action.affectedTargets = targets;
				Tools.undoRedoManager.AddAction(action, clearRedoActions);
			}


		}

		private void DeleteAllTargets() {
			foreach (Target target in notes) {
				//if (obj)
				Destroy(target.gridTargetIcon);
				Destroy(target.timelineTargetIcon);
			}

			//Second check through ordered notes to make sure they're all gone.
			foreach (Target obj in orderedNotes) {
				if (obj.gridTargetIcon)
					Destroy(obj.gridTargetIcon);
				if (obj.timelineTargetIcon)
					Destroy(obj.timelineTargetIcon);
			}

			//foreach (TimelineTarget obj in notesTimeline) {
			//    if (obj)
			//       Destroy(obj.gameObject);
			//}

			notes = new List<Target>();
			orderedNotes = new List<Target>();
			//notesTimeline = new List<Target>();
			selectableNotes = new List<Target>();
			loadedNotes = new List<Target>();

			var liner = gridTransformParent.gameObject.GetComponentInChildren<LineRenderer>();
			if (liner) {
				liner.SetPositions(new Vector3[0]);
				liner.positionCount = 0;
			}

			time = 0;
		}

		public void ChangeDifficulty() {
			// Debug.Log("ChangeDifficulty");
			// string dirpath = Application.persistentDataPath;

			// //string[] cuePath = Directory.GetFiles(dirpath + "\\temp\\", DifficultySelection_s.Value + ".cues");
			// if (cuePath.Length > 0) {
			// 	DeleteAllTargets();

			// 	//load cues from temp
			// 	string json = File.ReadAllText(cuePath[0]);
			// 	json = json.Replace("cues", "Items");
			// 	Cue[] cues = new Cue[2]; //JsonUtility.FromJson<Cue>(json);
			// 	foreach (Cue cue in cues) {
			// 		AddTarget(cue);
			// 	}
			// } else {
			// 	DeleteAllTargets();
			// }

			// foreach (GridTarget obj in notes) {
			// 	Debug.Log(obj);
			// }
		}

		public void Import() {
			DeleteAllTargets();

			//load cues from temp
			var cueFiles = StandaloneFileBrowser.OpenFilePanel("Import .cues", Application.persistentDataPath, "cues", false);
			if (cueFiles.Length > 0) {
				string json = File.ReadAllText(cueFiles[0]);
				json = json.Replace("cues", "Items");
				Cue[] cues = new Cue[2]; //JsonUtility.FromJson<Cue>(json);
				foreach (Cue cue in cues) {
					AddTarget(cue);
				}

				songLoaded = true;
			} else {
				Debug.Log("cues not found");

			}

		}

		public void Save() {
			Cue[] cues = new Cue[notes.Count];
			for (int i = 0; i < notes.Count; i++) {
				//cues[i] = orderedNotes[i].ToCue(offset);
			}

			string json = JsonUtility.ToJson(cues, true);
			json = json.Replace("Items", "cues");

			string dirpath = Application.persistentDataPath;

			if (notes.Count > 0)
				//File.WriteAllText(Path.Combine(dirpath + "\\temp\\", DifficultySelection_s.Value + ".cues"), json);

				//json = JsonUtility.ToJson(songDesc, true);
			File.WriteAllText(Path.Combine(dirpath + "\\temp\\", "song.desc"), json);
			FileInfo descFile = new FileInfo(Path.Combine(dirpath + "\\temp\\", "song.desc"));


			string[] oggPath = Directory.GetFiles(dirpath + "\\temp\\", "*.ogg");
			FileInfo oggFile = new FileInfo(oggPath[0]);

			List<FileInfo> files = new List<FileInfo>();
			files.Add(descFile);
			files.Add(oggFile);

			//push all .cues files to list
			var cueFiles = Directory.GetFiles(dirpath + "\\temp\\", "*.cues");
			if (cueFiles.Length > 0) {
				foreach (var cue in cueFiles) {
					FileInfo file = new FileInfo(cue);
					files.Add(file);
				}
			}

			string path = StandaloneFileBrowser.SaveFilePanel("Audica Save", Application.persistentDataPath, "Edica Save", "edica");
			//PlayerPrefs.SetString("previousSave", path);
			if (path.Length > 0)
				Compress(files, path);

		}

		public void Export() {
			string dirpath = Application.persistentDataPath;

			CueFile export = new CueFile();
			export.cues = new List<Cue>();

			foreach (GridTarget target in orderedNotes) {
				export.cues.Add(NotePosCalc.ToCue(target, offset, false));
			}

			audicaFile.diffs.expert = export;

			AudicaExporter.ExportToAudicaFile(audicaFile);


		}

		public void ExportAndPlay() {
			Export();
			string songFolder = PathLogic.GetSongFolder();
			File.Copy(audicaFile.filepath, Path.Combine(songFolder, audicaFile.desc.songID + ".audica"));

			string newPath = Path.GetFullPath(Path.Combine(songFolder, @"..\..\..\..\"));
			System.Diagnostics.Process.Start(Path.Combine(newPath, "Audica.exe"));
		}

		public void ExportCueToTemp() {
			Cue[] cues = new Cue[notes.Count];
			for (int i = 0; i < notes.Count; i++) {
				//cues[i] = orderedNotes[i].ToCue(offset);
			}

			string json = JsonUtility.ToJson(cues, true);
			json = json.Replace("Items", "cues");

			string dirpath = Application.persistentDataPath;

			//if (notes.Count > 0)
			//File.WriteAllText(Path.Combine(dirpath + "\\temp\\", DifficultySelection_s.Value + ".cues"), json);
		}

		public void Compress(List<FileInfo> files, string destination) {
			string dirpath = Application.persistentDataPath;

			if (!System.IO.Directory.Exists(dirpath + "\\TempSave\\")) {
				//System.IO.Directory.CreateDirectory(dirpath + "\\TempSave\\", securityRules);

			}

			foreach (FileInfo fileToCompress in files) {
				fileToCompress.CopyTo(dirpath + "\\TempSave\\" + fileToCompress.Name, true);
			}

			try {
				ZipFile.CreateFromDirectory(dirpath + "\\TempSave\\", destination);
			} catch (IOException e) {
				Debug.Log(e.Message + "....Deleting File");
				FileInfo fileToReplace = new FileInfo(destination);
				fileToReplace.Delete();
				try {
					ZipFile.CreateFromDirectory(dirpath + "\\TempSave\\", destination);
				} catch (IOException e2) {
					Debug.Log(e2.Message + "....No More attempts");
				}

			}
			DirectoryInfo dir = new DirectoryInfo(dirpath + "\\TempSave\\");
			dir.Delete(true);
		}

		public void NewFile() {
			DeleteAllTargets();

			notes = new List<Target>();
			//notesTimeline = new List<TimelineTarget>();

			string dirpath = Application.persistentDataPath;

			//create the new song desc
			//songDesc = new SongDescyay();

			//locate and copy ogg file to temp folder (used to save the project later)
			var audioFiles = StandaloneFileBrowser.OpenFilePanel("Import .ogg file", System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyMusic), "ogg", false);
			if (audioFiles.Length > 0) {
				FileInfo oggFile = new FileInfo(audioFiles[0]);
				if (!System.IO.Directory.Exists(dirpath + "\\temp\\")) {
					//System.IO.Directory.CreateDirectory(dirpath + "\\temp\\", securityRules);
				} else {
					DirectoryInfo direct = new DirectoryInfo(dirpath + "\\temp\\");
					direct.Delete(true);

					//System.IO.Directory.CreateDirectory(dirpath + "\\temp\\", securityRules);
				}
				oggFile.CopyTo(dirpath + "\\temp\\" + oggFile.Name, true);

				//load ogg into audio clip
				if (audioFiles.Length > 0) {
					StartCoroutine(GetAudioClip(audioFiles[0]));
				} else {
					Debug.Log("ogg not found");
				}
			} else {
				Application.Quit();
			}

		}

		public void LoadAudicaFile(bool loadRecent = false, string filePath = null) {

			//if (audicaFile != null) return;

			DeleteAllTargets();

			if (loadRecent) {
				audicaFile = AudicaHandler.LoadAudicaFile(PlayerPrefs.GetString("recentFile", null));
				if (audicaFile == null) return;

			} else if (filePath != null) {
				audicaFile = AudicaHandler.LoadAudicaFile(filePath);

			} else {

				string[] paths = StandaloneFileBrowser.OpenFilePanel("Audica File (Not OST)", Path.Combine(Application.persistentDataPath), "audica", false);

				audicaFile = AudicaHandler.LoadAudicaFile(paths[0]);
				PlayerPrefs.SetString("recentFile", paths[0]);
			}

			SetOffset(audicaFile.desc.offset);


			//Loads all the sounds.
			StartCoroutine(GetAudioClip($"file://{Application.dataPath}/.cache/{audicaFile.desc.cachedMainSong}.ogg"));
			StartCoroutine(LoadLeftSustain($"file://{Application.dataPath}/.cache/{audicaFile.desc.cachedSustainSongLeft}.ogg"));
			StartCoroutine(LoadRightSustain($"file://{Application.dataPath}/.cache/{audicaFile.desc.cachedSustainSongRight}.ogg"));

			foreach (Cue cue in audicaFile.diffs.expert.cues) {
				AddTarget(cue);
			}

			if (orderedNotes.Count > 0) {
				/*

				foreach (var note in orderedNotes) {

					if (note.behavior == TargetBehavior.ChainStart && note) {
						if (note.chainedNotes.Count > 0) {
							if ((note.transform.position.z < 0 && 0 > note.chainedNotes.Last().transform.position.z) || (note.transform.position.z > 0 && 0 < note.chainedNotes.Last().transform.position.z)) {
								note.GetComponentsInChildren<LineRenderer>() [1].enabled = false;
							} else {
								note.GetComponentsInChildren<LineRenderer>() [1].enabled = true;
							}
						}
					}

					if (note.behavior == TargetBehavior.Hold && note) {
						var length = Mathf.Ceil(note.GetComponentInChildren<HoldTargetManager>().sustainLength);
						if (note.gridTarget.beatLength != length) {

							note.gridTarget.SetBeatLength(length);
						}
					}
				}
				*/
			}

		}

		public void LoadFromFile(string edicaSave) {
			DeleteAllTargets();

			if (edicaSave.Length > 0) {
				PlayerPrefs.SetString("previousSave", edicaSave);

				//move zip to temp folder
				string dirpath = Application.persistentDataPath;
				if (!System.IO.Directory.Exists(dirpath + "\\temp\\")) {
					//System.IO.Directory.CreateDirectory(dirpath + "\\temp\\", securityRules);
				} else {
					DirectoryInfo direct = new DirectoryInfo(dirpath + "\\temp\\");
					direct.Delete(true);

					//System.IO.Directory.CreateDirectory(dirpath + "\\temp\\", securityRules);
				}

				//unzip save into temp
				ZipFile.ExtractToDirectory(edicaSave, dirpath + "\\temp\\");

				//load desc from temp
				var descFiles = Directory.GetFiles(dirpath + "\\temp\\", "song.desc");
				if (descFiles.Length > 0) {
					string json = File.ReadAllText(dirpath + "\\temp\\song.desc");
					//songDesc = JsonUtility.FromJson<SongDescyay>(json);
					//SetOffset(songDesc.offset);
					//SetSongID(songDesc.songID);
					//SetSongTitle(songDesc.title);
					//SetSongArtist(songDesc.artist);
					//SetSongEndEvent(songDesc.songEndEvent.Replace("event:/song_end/song_end_", string.Empty));
					//SetSongPreRoll(songDesc.prerollSeconds);
					//SetSongAuthor(songDesc.author);

				} else {
					Debug.Log("desc not found");
					//songDesc = new SongDescyay();
				}

				//load cues from temp
				//var cueFile = Directory.GetFiles(dirpath + "\\temp\\", DifficultySelection_s.Value + ".cues");
				var cueFiles = Directory.GetFiles(dirpath + "\\temp\\", "*.cues");
				if (cueFiles.Length > 0) {
					//figure out if it has difficulties (new edica file)
					bool isDifficultyNamed = false;
					string difficulty = "";
					foreach (var file in cueFiles) {
						if (file.Contains("expert.cues")) {
							isDifficultyNamed = true;
							difficulty = "expert";
						} else if (file.Contains("advanced.cues")) {
							isDifficultyNamed = true;
							difficulty = "advanced";
						} else if (file.Contains("moderate.cues")) {
							isDifficultyNamed = true;
							difficulty = "moderate";
						} else if (file.Contains("beginner.cues")) {
							isDifficultyNamed = true;
							difficulty = "beginner";
						}
					}

					if (isDifficultyNamed) {
						//DifficultySelection_s.Value = difficulty;
						//DifficultySelection_s.selection.value = (int) DifficultySelection.Options.Parse(typeof(DifficultySelection.Options), difficulty);

						//load .cues (delete first because changing difficulty creates targets)
						DeleteAllTargets();
						var cueFile = Directory.GetFiles(dirpath + "\\temp\\", difficulty + ".cues");
						string json = File.ReadAllText(cueFile[0]);
						//json = json.Replace("cues", "Items");
						CueFile cues = JsonUtility.FromJson<CueFile>(json);
						foreach (Cue cue in cues.cues) {
							AddTarget(cue);
						}
					} else //old edica format. Shove it into expert
					{
						string json = File.ReadAllText(cueFiles[0]);
						json = json.Replace("cues", "Items");
						CueFile cues = JsonUtility.FromJson<CueFile>(json);
						foreach (Cue cue in cues.cues) {
							AddTarget(cue);
						}
					}

				} else {
					Debug.Log("cues not found");
					NewFile();
				}

				//load ogg into audio clip
				var audioFiles = Directory.GetFiles(dirpath + "\\temp\\", "*.ogg");
				if (audioFiles.Length > 0) {
					StartCoroutine(GetAudioClip(audioFiles[0]));
				} else {
					Debug.Log("ogg not found");
				}
			} else {
				NewFile();
			}
		}
		public void LoadPrevious() {
			string edicaSave = PlayerPrefs.GetString("previousSave");;
			LoadFromFile(edicaSave);
		}

		public void Load() {
			//open save
			string[] edicaSave = StandaloneFileBrowser.OpenFilePanel("Edica save file", Application.persistentDataPath, "edica", false);
			if (edicaSave.Length > 0)
				LoadFromFile(edicaSave[0]);
			else
				NewFile();
		}

		IEnumerator GetAudioClip(string uri) {
			using(UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.OGGVORBIS)) {
				yield return www.SendWebRequest();

				if (www.isNetworkError) {
					Debug.Log(www.error);
				} else {
					AudioClip myClip = DownloadHandlerAudioClip.GetContent(www);
					aud.clip = myClip;
					previewAud.clip = myClip;


					SetBPM((float) audicaFile.desc.tempo);
					//SetScale(20);
					//Resources.FindObjectsOfTypeAll<OptionsMenu>().First().Init(bpm, offset, beatSnap, songid, songtitle, songartist, songendevent, songpreroll, songauthor);

					spectrogram.GetComponentInChildren<AudioWaveformVisualizer>().Init();


				}
			}
		}

		IEnumerator LoadLeftSustain(string uri) {
			Debug.Log("Loading left sustian.");
			using(UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.OGGVORBIS)) {
				yield return www.SendWebRequest();

				if (www.isNetworkError) {
					Debug.Log(www.error);
				} else {
					AudioClip myClip = DownloadHandlerAudioClip.GetContent(www);
					leftSustainAud.clip = myClip;
					leftSustainAud.volume = 0f;
					Debug.Log("Left sustain loaded.");
				}
			}
		}
		IEnumerator LoadRightSustain(string uri) {
			using(UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.OGGVORBIS)) {
				yield return www.SendWebRequest();

				if (www.isNetworkError) {
					Debug.Log(www.error);
				} else {
					AudioClip myClip = DownloadHandlerAudioClip.GetContent(www);
					rightSustainAud.clip = myClip;
					rightSustainAud.volume = 0f;

				}
			}
		}


		public void UpdateSongDesc(string songID, string title, int bpm, string songEndEvent = "C#", string mapper = "", int offset = 0) {
			audicaFile.desc.songID = songID;
			audicaFile.desc.title = title;
			audicaFile.desc.tempo = bpm;
			audicaFile.desc.songEndEvent = songEndEvent;
			audicaFile.desc.mapper = mapper;
			audicaFile.desc.offset = offset;
			SetBPM(bpm);
			SetOffset(offset);
		}


		public void SetBehavior(TargetBehavior behavior) {
			selectedBehaviour = behavior;
		}

		public void SetVelocity(TargetVelocity velocity) {
			selectedVelocity = velocity;
		}

		public void SetPlaybackSpeed(float speed) {
			playbackSpeed = speed;
			aud.pitch = speed;
			previewAud.pitch = speed;
		}

		public void SetHandType(TargetHandType handType) {
			selectedHandType = handType;
		}

		public void SetBPM(float newBpm) {
			bpm = newBpm;
			//TODO:
			audicaFile.desc.tempo = newBpm;
			SetScale(scale);
		}

		public void SetOffset(int newOffset) {
			offset = newOffset;
			//songDesc.offset = newOffset;
			audicaFile.desc.offset = newOffset;
		}

		public void SetSnap(int newSnap) {
			beatSnap = newSnap;
		}
		public void BeatSnapChanged() {
			string temp = beatSnapSelector.elements[beatSnapSelector.index];
			int snap = 4;
			int.TryParse(temp.Substring(2), out snap);
			beatSnap = snap;
		}

		// .desc menu
		public void SetSongID(string newSongID) {
			//songid = newSongID;
			//songDesc.songID = newSongID;
			audicaFile.desc.songID = newSongID;
		}

		public void SetSongTitle(string newSongTitle) {
			//songtitle = newSongTitle;
			//songDesc.title = newSongTitle;
			audicaFile.desc.title = newSongTitle;
		}

		public void SetSongArtist(string newSongArtist) {
			//songartist = newSongArtist;
			//songDesc.artist = newSongArtist;
			audicaFile.desc.artist = newSongArtist;
		}

		public void SetSongEndEvent(string newSongEndEvent) {
			//songendevent = newSongEndEvent;
			//songDesc.songEndEvent = string.Concat("event:/song_end/song_end_", newSongEndEvent.ToUpper());
			audicaFile.desc.songEndEvent = string.Concat("event:/song_end/song_end_", newSongEndEvent.ToUpper());
		}

		public void SetSongPreRoll(float newSongPreRoll) {
			//songpreroll = newSongPreRoll;
			//songDesc.prerollSeconds = newSongPreRoll;
			audicaFile.desc.prerollSeconds = newSongPreRoll;
		}

		public void SetSongAuthor(string newSongAuthor) {
			//songauthor = newSongAuthor;
			//songDesc.author = newSongAuthor;
			audicaFile.desc.mapper = newSongAuthor;
		}

		// public void SetMogg(string newmogg) {
		//     if (!mogg.Contains(".mogg")) {
		//         newmogg += ".mogg";
		//     }

		//     mogg = newmogg;
		//     songDesc.moggSong = mogg;

		// }

		public void SetBeatTime(float t) {
			timelineBG.material.SetTextureOffset("_MainTex", new Vector2(((t * bpm / 60 - offset / 480f) / 4f + scaleOffset), 1));

			timelineTransformParent.transform.localPosition = Vector3.left * (t * bpm / 60 - offset / 480f) / (scale / 20f);
			spectrogram.localPosition = Vector3.left * (t * bpm / 60) / (scale / 20f);

			gridTransformParent.transform.localPosition = Vector3.back * (t * bpm / 60 - offset / 480f);
		}

		public void SetScale(int newScale) {
			timelineBG.material.SetTextureScale("_MainTex", new Vector2(newScale / 4f, 1));
			scaleOffset = -newScale % 8 / 8f;

			spectrogram.localScale = new Vector3(aud.clip.length / 2 * bpm / 60 / (newScale / 20f), 1, 1);

			timelineTransformParent.transform.localScale *= (float) scale / newScale;
			targetScale *= (float) newScale / scale;
			// fix scaling on all notes
			foreach (Transform note in timelineTransformParent.transform) {
				note.localScale = targetScale * Vector3.one;
			}
			scale = newScale;
		}

		public void UpdateTrail() {
			Vector3[] positions = new Vector3[gridTransformParent.childCount];
			for (int i = 0; i < gridTransformParent.transform.childCount; i++) {
				positions[i] = gridTransformParent.GetChild(i).localPosition;
			}
			positions = positions.OrderBy(v => v.z).ToArray();
			var liner = gridTransformParent.gameObject.GetComponentInChildren<LineRenderer>();
			liner.positionCount = gridTransformParent.childCount;
			liner.SetPositions(positions);
		}

		IEnumerator CalculateNoteCollidersEnabled() {

			int framesToSplitOver = 50;

			int amtToCalc = Mathf.RoundToInt(orderedNotes.Count / framesToSplitOver);

			int j = 0;

			for (int i = 0; i < framesToSplitOver; i++) {

				while(j < orderedNotes.Count) {

					float targetPos = orderedNotes[j].gridTargetIcon.transform.position.z;

					if (targetPos > -20 && targetPos < 20) {
						orderedNotes[j].gridTargetIcon.sphereCollider.enabled = true;
					} else {
						orderedNotes[j].gridTargetIcon.sphereCollider.enabled = false;
					}

					
					if (j > amtToCalc * (i + 1)) break;

					j++;
				}



				yield return null;
				
			}

			while (j < orderedNotes.Count) {
				float targetPos = orderedNotes[j].gridTargetIcon.transform.position.z;

				if (targetPos > -20 && targetPos < 20) {
					orderedNotes[j].gridTargetIcon.sphereCollider.enabled = true;
				} else {
					orderedNotes[j].gridTargetIcon.sphereCollider.enabled = false;
				}
				j++;
			}
			//yield return new WaitForSeconds(1);
			StartCoroutine(CalculateNoteCollidersEnabled());

		}

		public void Update() {
			//TODO: Ordrerd
			/*
			if (orderedNotes.Count > 0) {
				foreach (var note in orderedNotes) {

					if (note.behavior == TargetBehavior.ChainStart && note) {
						if (note.chainedNotes.Count > 0) {
							if ((note.transform.position.z < 0 && 0 > note.chainedNotes.Last().transform.position.z) || (note.transform.position.z > 0 && 0 < note.chainedNotes.Last().transform.position.z)) {
								note.GetComponentsInChildren<LineRenderer>() [1].enabled = false;
							} else {
								note.GetComponentsInChildren<LineRenderer>() [1].enabled = true;
							}
						}
					}

					if (note.behavior == TargetBehavior.Hold && note) {
						float temp = 480;
						float.TryParse(note.GetComponentInChildren<HoldController>().length.text, out temp);
						var length = Mathf.Ceil(temp) / 480;
						if (note.gridTarget.beatLength != length) {
							//note.gridTarget.SetBeatLength(length);
							//Debug.Log(note.beatLength);
						}
					}
				}
			}
			*/

			//Failed attempt at optimizing:

			//float test = BeatTime();
			//foreach (Target note in loadedNotes) {


				//if (note.gridTargetIcon.transform.position.z > -5 && note.gridTargetIcon.transform.position.z < 5) {
				//	note.gridTargetIcon.sphereCollider.enabled = true;
				//} else {
				//	note.gridTargetIcon.sphereCollider.enabled = false;
				//}
			//}

			UpdateChords();

			if (SustainParticles)
				UpdateSustains();

			if (!paused) time += Time.deltaTime * playbackSpeed;

			if (hover) {
				if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) {
					if (Input.mouseScrollDelta.y > 0.1f) {
						SetScale(scale - 1);
					} else if (Input.mouseScrollDelta.y < -0.1f) {
						SetScale(scale + 1);
					}
				}
			}
			//if (!(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) && hover))

			if (Input.mouseScrollDelta.y < -0.1f) {
				time += BeatsToDuration(4f / beatSnap);
				time = SnapTime(time);

				SafeSetTime();
				if (paused) previewAud.Play();
			} else if (Input.mouseScrollDelta.y > 0.1f) {
				time -= BeatsToDuration(4f / beatSnap);
				time = SnapTime(time);
				SafeSetTime();
				if (paused) previewAud.Play();
			}

			if (Input.GetKeyDown(KeyCode.Space)) {

			}

			SetBeatTime(time);
			if (previewAud.time > time + previewDuration) {
				previewAud.Pause();
			}

			previewAud.volume = aud.volume;

			SetCurrentTime();
			SetCurrentTick();

			//bottomTimelineSlider.SetValueWithoutNotify(GetPercentagePlayed());

			miniTimeline.SetPercentagePlayed(GetPercentagePlayed());
		}


		public void JumpToPercent(float percent) {

			time = SnapTime(aud.clip.length * percent);

			SafeSetTime();
			SetCurrentTime();
			SetCurrentTick();
		}


		public void TogglePlayback() {
			if (paused) {
				aud.Play();
				//metro.StartMetronome();

				previewAud.Pause();

				if (time < leftSustainAud.clip.length) {
					leftSustainAud.Play();
					rightSustainAud.Play();

				}

				paused = false;
			} else {
				aud.Pause();
				leftSustainAud.Pause();
				rightSustainAud.Pause();
				paused = true;

				//Uncommented
				//time = SnapTime(time);
				if (time < 0) time = 0;
				if (time > aud.clip.length) time = aud.clip.length;

			}
		}

		private void SafeSetTime() {
			if (time < 0) time = 0;

			if (time > aud.clip.length) {
				time = aud.clip.length;
			}
			aud.time = time;
			previewAud.time = time;

			float tempTime = time;
			if (time > leftSustainAud.clip.length) {
				tempTime = leftSustainAud.clip.length;
			}
			leftSustainAud.time = tempTime;

			if (time > rightSustainAud.clip.length) {
				tempTime = rightSustainAud.clip.length;
			}
			rightSustainAud.time = tempTime;
		}

		void OnMouseOver() {
			hover = true;
		}

		void OnMouseExit() {
			hover = false;
		}

		public float GetPercentagePlayed() {
			if (aud.clip != null)
				return (time / aud.clip.length);

			else
				return 0;
		}

		public static float DurationToBeats(float t) {
			return t * bpm / 60;
		}

		public float BeatsToDuration(float beat) {
			return beat * 60 / bpm;
		}

		public float Snap(float beat) {
			return Mathf.Round(beat * beatSnap / 4f) * 4f / beatSnap;
		}

		public static float BeatTime() {
			return DurationToBeats(time) - offset / 480f;
		}

		public float SnapTime(float t) {
			return BeatsToDuration(Snap(DurationToBeats(t) - offset / 480f) + offset / 480f);
		}

		private void SetCurrentTime() {
			string minutes = Mathf.Floor((int) time / 60).ToString("00");
			string seconds = ((int) time % 60).ToString("00");

			songTimestamp.text = minutes + ":" + seconds;
			//curSeconds.text = seconds;
		}

		private void SetCurrentTick() {
			string currentTick = Mathf.Floor((int) BeatTime() * 480f).ToString();

			curTick.text = currentTick;
		}

		public void SustainParticlesToggle(Toggle tog) {
			SustainParticles = tog.isOn;

			if (!SustainParticles) {
				foreach (var note in orderedNotes) {
					if (note.behavior == TargetBehavior.Hold) {
						//var particles = note.GetComponentInChildren<ParticleSystem>();
						//if (particles.isEmitting) {
						//    particles.Stop();
						//}
					}
				}
			}
		}
	}
}