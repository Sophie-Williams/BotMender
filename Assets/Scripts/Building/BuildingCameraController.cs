﻿using UnityEngine;

namespace Building {
	/// <summary>
	/// Controls the camera it is attached to, should be used in the build mode.
	/// </summary>
	public class BuildingCameraController : MonoBehaviour {
		public const float PitchFactor = 1.3f;
		public const float YawFactor = 1.3f;
		public const float HorizontalFactor = 0.25f;
		public const float VerticalFactor = 0.25f;

		private float _pitch;
		private float _yaw;

		private void Start() {
			Vector3 euler = transform.rotation.eulerAngles;
			_pitch = euler.x;
			_yaw = euler.y;
		}



		private void FixedUpdate() {
			if (Cursor.lockState == CursorLockMode.None) {
				return;
			}

			_pitch += Input.GetAxisRaw("MouseY") * PitchFactor;
			if (_pitch < -90) {
				_pitch = -90;
			} else if (_pitch > 90) {
				_pitch = 90;
			}

			_yaw = (_yaw + Input.GetAxisRaw("MouseX") * YawFactor) % 360;
			transform.rotation = Quaternion.Euler(_pitch, _yaw, 0);

			transform.position += transform.rotation * new Vector3(
					Input.GetAxisRaw("Rightward") * HorizontalFactor,
					Input.GetAxisRaw("Upward") * VerticalFactor,
					Input.GetAxisRaw("Forward") * HorizontalFactor
			);
		}
	}
}
