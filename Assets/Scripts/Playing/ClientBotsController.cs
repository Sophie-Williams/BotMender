﻿using Systems.Weapon;
using Networking;
using UnityEngine;

namespace Playing {
	/// <summary>
	/// Handles all networked bots on the client-side.
	/// Only a single instance of this behaviour should be present at once.
	/// </summary>
	public class ClientBotsController : MonoBehaviour {
		private void Start() {
			NetworkClient.SetTcpHandler(TcpPacketType.Server_System_Execute,
				buffer => ((WeaponSystem)BotCache.Get(buffer.ReadByte()).TryGetSystem(buffer.ReadByte()))
					.ClientExecuteWeaponFiring(buffer));
		}

		private void OnDestroy() {
			NetworkClient.SetTcpHandler(TcpPacketType.Server_System_Execute, null);
		}
	}
}
