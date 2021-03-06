﻿using System.Collections.Generic;
using Blocks.Live;
using Structures;
using UnityEngine;

namespace Systems.Weapon {
	/// <summary>
	/// A hitscan weapon which fires a laser pulse which only damages the block it hits.
	/// It has a visualized travel time.
	/// </summary>
	public class LaserWeapon : HitscanWeapon {
		public const float MaxParticleLifeTime = 5f;
		private readonly ParticleSystem _particles;

		public LaserWeapon(CompleteStructure structure, RealLiveBlock block, WeaponConstants constants)
			: base(structure, block, constants) {
			_particles = Turret.GetComponent<ParticleSystem>();
			ParticleSystem.ShapeModule shape = _particles.shape;
			shape.position = Constants.TurretOffset;
		}



		protected override void ServerFireWeapon(Vector3 point, RealLiveBlock block) {
			if (block != null) {
				block.GetComponentInParent<CompleteStructure>()
					.DamagedServer(new[] {new KeyValuePair<RealLiveBlock, uint>(block, block.Damage(400))});
			}
		}

		protected override void ClientFireWeapon(Vector3 point) {
			ParticleSystem.ShapeModule shape = _particles.shape;
			Vector3 path = point - TurretEnd;
			shape.rotation = (Quaternion.Inverse(_particles.transform.rotation) * Quaternion.LookRotation(path)).eulerAngles;

			ParticleSystem.MainModule main = _particles.main;
			main.startLifetime = new ParticleSystem.MinMaxCurve(Mathf.Min(MaxParticleLifeTime,
				path.magnitude / main.startSpeed.Evaluate(0)));
			_particles.Emit(1);
		}
	}
}
