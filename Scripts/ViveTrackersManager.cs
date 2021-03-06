﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Valve.VR;

namespace ViveTrackers
{
	/// <summary>
	/// This class is used to manage Vive Tracker devices using OpenVR API.
	/// To run correctly, this class needs SteamVR application to run on the same computer.
	/// 1) To create the trackers, call the RefreshTrackers() method. This method can be called multiple times during runtime.
	/// - You can define a restricted set of Vive Tracker to create during runtime using the config file ViveTrackers.csv.
	/// - Using the config file or not, only the available connected devices in SteamVR are instantiated during runtime.
	/// 2) Once the trackers are created, you can update trackers'transforms using the UpdateTrackers() method.
	/// Example of config file content (# is used to comment):
	/// SerialNumber;Name;
	/// LHR-5850D511;A;
	/// LHR-9F7F5582;B;
	/// #LHR-3CECF391;C;
	/// #LHR-D5918492;D;
	/// #LHR-AC3ABE2E;E;
	/// </summary>
	public sealed class ViveTrackersManager : MonoBehaviour
	{
		[Tooltip("Template used to instantiate available trackers")]
		public ViveTracker prefab;
		[Tooltip("The origin of the tracking space (+ used as the default rotation to calibrate Trackers' rotations")]
		public DebugTransform origin;
		[Tooltip("The path of the file containing the list of the restricted set of trackers to use")]
		public string configFilePath = "ViveTrackers.csv";
		[Tooltip("True, to create only the trackers declared in the config file. False, to create all connected trackers available in SteamVR.")]
		public bool createDeclaredTrackersOnly = false;
		[Tooltip("Log tracker detection or not. Useful to discover trackers' serial numbers")]
		public bool logTrackersDetection = true;

		private bool _ovrInit = false;
		private CVRSystem _cvrSystem = null;
		// Trackers declared in config file [TrackedDevice_SerialNumber, Name]
		private Dictionary<string, string> _declaredTrackers = new Dictionary<string, string>();
		// All trackers found during runtime (no duplicates), after successive calls to RefreshTrackers().
		private List<ViveTracker> _trackers = new List<ViveTracker>();
		// Poses for all tracked devices in OpenVR (HMDs, controllers, trackers, etc...).
		private TrackedDevicePose_t[] _ovrTrackedDevicePoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

		public Action<List<ViveTracker>> TrackersFound; // This callback is called everytime you call the RefreshTrackers() method.

		private void Awake()
		{
			// Read config file
			using (StreamReader reader = File.OpenText(configFilePath))
			{
				// Read Header
				string line = reader.ReadLine();
				// Read Data
				while ((line = reader.ReadLine()) != null)
				{
					string[] items = line.Split(';');
					if (!items[0].Contains("#"))
					{
						_declaredTrackers.Add(items[0], items[1]);
					}
				}
			}
			Debug.Log("[ViveTrackersManager] " + _declaredTrackers.Count + " trackers declared in config file : " + configFilePath);
		}

		public void SetDebugActive(bool pActive)
		{
			origin.SetDebugActive(pActive);
			foreach (ViveTracker tracker in _trackers)
			{
				tracker.debugTransform.SetDebugActive(pActive);
			}
		}

		public bool IsTrackerConnected(string pTrackerName)
		{
			if (!_ovrInit)
			{
				return false;
			}
			ViveTracker tracker = _trackers.Find(trck => trck.name == pTrackerName);
			return (tracker != null) && tracker.IsConnected;
		}

		/// <summary>
		/// Update ViveTracker transforms using the corresponding Vive Tracker devices.
		/// </summary>
		public void UpdateTrackers()
		{
			if (!_ovrInit)
			{
				return;
			}
			// Fetch last Vive Tracker devices poses.
			_cvrSystem.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0, _ovrTrackedDevicePoses);
			// Apply poses to ViveTracker objects.
			foreach (var tracker in _trackers)
			{
				TrackedDevicePose_t pose = _ovrTrackedDevicePoses[tracker.ID.TrackedDevice_Index];
				if (pose.bDeviceIsConnected && pose.bPoseIsValid)
				{
					tracker.UpdateTransform(new SteamVR_Utils.RigidTransform(pose.mDeviceToAbsoluteTracking));
				}
			}
		}

		/// <summary>
		/// Align all trackers' transformation with origin's transformation.
		/// </summary>
		public void CalibrateTrackers()
		{
			foreach (var tracker in _trackers)
			{
				tracker.Calibrate();
			}
		}

		/// <summary>
		/// Scan for available Vive Tracker devices and creates ViveTracker objects accordingly.
		/// Init OpenVR if not already done.
		/// </summary>
		public void RefreshTrackers()
		{
			if (!_ovrInit)
			{
				_ovrInit = _InitOpenVR();
				if (!_ovrInit)
				{
					return;
				}
			}

			Debug.Log("[ViveTrackersManager] Scanning for Tracker devices...");
			for (uint i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; ++i)
			{
				ETrackedDeviceClass deviceClass = _cvrSystem.GetTrackedDeviceClass(i);
				if (deviceClass == ETrackedDeviceClass.GenericTracker)
				{
					string sn = _GetTrackerSerialNumber(i);
					if (logTrackersDetection)
					{
						Debug.Log("[ViveTrackersManager] Tracker detected : " + sn);
					}

					if (sn != "")
					{
						// Creates tracker object if not already existing.
						if (!_trackers.Exists(tracker => tracker.ID.TrackedDevice_SerialNumber == sn))
						{
							string trackerName = "";
							bool declared = _declaredTrackers.TryGetValue(sn, out trackerName);
							// Creates only trackers declared in config file or all (if !createDeclaredTrackersOnly).
							if (declared || !createDeclaredTrackersOnly)
							{
								ViveTracker vt = GameObject.Instantiate<ViveTracker>(prefab, origin.transform.position, origin.transform.rotation, origin.transform);
								vt.Init(_cvrSystem, new ViveTrackerID(i, sn), declared ? trackerName : sn);
								_trackers.Add(vt);
							}
						}
					}
				}
			}

			// Check
			if (_trackers.Count == 0)
			{
				Debug.LogWarning("[ViveTrackersManager] No trackers available !");
				return;
			}

			// Sort Trackers by name.
			_trackers.Sort((ViveTracker x, ViveTracker y) => { return string.Compare(x.name, y.name); });

			Debug.Log(string.Format("[ViveTrackersManager] {0} trackers declared and {1} trackers available:", _declaredTrackers.Count, _trackers.Count));
			foreach (var tracker in _trackers)
			{
				Debug.Log(string.Format("[ViveTrackersManager] -> Tracker : Name = {0} ; SN = {1} ; Index = {2}", tracker.name, tracker.ID.TrackedDevice_SerialNumber, tracker.ID.TrackedDevice_Index));
			}

			// Fire Action.
			if (TrackersFound != null)
			{
				TrackersFound(_trackers);
			}
		}

		private bool _InitOpenVR()
		{
			// OpenVR Init
			EVRInitError error = EVRInitError.None;
			_cvrSystem = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Other);
			if (error != EVRInitError.None)
			{
				Debug.LogError("[ViveTrackersManager] OpenVR Error : " + error);
				return false;
			}
			Debug.Log("[ViveTrackersManager] OpenVR initialized.");
			return true;
		}

		private string _GetTrackerSerialNumber(uint pTrackerIndex)
		{
			string sn = "";
			ETrackedPropertyError error = new ETrackedPropertyError();
			StringBuilder sb = new StringBuilder();
			_cvrSystem.GetStringTrackedDeviceProperty(pTrackerIndex, ETrackedDeviceProperty.Prop_SerialNumber_String, sb, OpenVR.k_unMaxPropertyStringSize, ref error);
			if (error == ETrackedPropertyError.TrackedProp_Success)
			{
				sn = sb.ToString();
			}
			return sn;
		}
	}
}