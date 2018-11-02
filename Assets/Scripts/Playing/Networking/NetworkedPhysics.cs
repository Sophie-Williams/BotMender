﻿using System.Collections.Generic;
using System.Linq;
using DoubleSocket.Protocol;
using DoubleSocket.Utility.BitBuffer;
using Networking;
using Structures;
using UnityEngine;
using Utilities;

namespace Playing.Networking {
	/// <summary>
	/// A class which handles the physics in a networked situation.
	/// Since there is no demand for a change, currently only CompleteStructures can be networked.
	/// All of them need to be registered in this class.
	/// 
	/// This class takes over the UDP packet creation and handling of the client and/or the server.
	/// This control is released when the behaviour is destroyed.
	/// 
	/// Client-to-server UDP packet contents: PlayerInput
	/// Server-to-client UDP packet contents: compressed timestamp + player count * BotState
	/// </summary>
	public class NetworkedPhysics : MonoBehaviour {
		public const int TimestepMillis = 20;
		public const float TimestepSeconds = 0.02f;
		private const float MaxNonNetDelayMillis = TimestepMillis + 1000f / NetworkUtils.UdpSendFrequency;
		private const float AverageNonNetDelayMillis = MaxNonNetDelayMillis / 2;

		/// <summary>
		/// Creates a new GameObject containing this component and also initializes, returns itself.
		/// </summary>
		public static NetworkedPhysics Create() {
			NetworkedPhysics instance = new GameObject("NetworkedPhysics").AddComponent<NetworkedPhysics>();
			if (NetworkUtils.IsServer) {
				NetworkClient.UdpHandler = buffer => { };
				NetworkServer.UdpHandler = (sender, buffer) => BotCache.SetExtra(sender.Id,
					BotCache.Extra.NetworkedPhysics, buffer.Array);
			} else {
				NetworkClient.UdpHandler = buffer => instance._lastClientUdpPacket = buffer.Array;
			}
			return instance;
		}



		private readonly MutableBitBuffer _sharedBuffer = new MutableBitBuffer();
		private readonly IDictionary<long, GuessedInput> _guessedInputs = new SortedDictionary<long, GuessedInput>();
		private readonly BotState _tempBotState = new BotState();
		private long _silentSkipFastForwardUntil;
		private byte[] _lastClientUdpPacket;

		private void OnDestroy() {
			NetworkClient.UdpHandler = buffer => { };
			NetworkServer.UdpHandler = (sender, buffer) => { };
			NetworkClient.UdpPayload = null;
			NetworkServer.UdpPayload = null;
			BotCache.ClearExtra(BotCache.Extra.NetworkedPhysics);
		}



		/// <summary>
		/// Handles the input change of the local player.
		/// Not all input is specified in the parameters.
		/// </summary>
		public void UpdateLocalInput(Vector3 trackedPosition) {
			Vector3 movementInput = new Vector3(Input.GetAxisRaw("Rightward"),
				Input.GetAxisRaw("Upward"), Input.GetAxisRaw("Forward"));
			byte[] payload = new byte[(BotState.InputSerializedBitsSize + 7) / 8];
			_sharedBuffer.ClearContents(payload);
			BotState.SerializePlayerInput(_sharedBuffer, movementInput, trackedPosition);

			if (NetworkUtils.IsServer) {
				BotCache.SetExtra(NetworkUtils.LocalId, BotCache.Extra.NetworkedPhysics, payload);
			} else {
				NetworkClient.UdpPayload = payload;
				long key = DoubleProtocol.TimeMillis + Mathf.RoundToInt(NetworkClient.UdpNetDelay + AverageNonNetDelayMillis);
				_guessedInputs.Remove(key);
				_guessedInputs.Add(key, new GuessedInput(movementInput));
			}
		}



		private void FixedUpdate() {
			if (NetworkUtils.IsServer) {
				ServerFixedUpdate();
			} else {
				ClientOnlyFixedUpdate();
			}
		}

		private void ServerFixedUpdate() {
			BotCache.ForEachId(id => {
				byte[] packet = BotCache.TakeExtra<byte[]>(id, BotCache.Extra.NetworkedPhysics);
				if (packet == null) {
					return;
				}

				_sharedBuffer.SetContents(packet);
				if (_sharedBuffer.TotalBitsLeft >= BotState.InputSerializedBitsSize) {
					BotState.DeserializePlayerInput(_sharedBuffer, out Vector3 movementInput, out Vector3 trackedPosition);
					BotCache.Get(id).UpdateInputOnly(movementInput, trackedPosition);
				}
			});
			Simulate(TimestepMillis);

			if (BotCache.Count > 0) {
				int bitSize = 48 + BotState.SerializedBitsSize * BotCache.Count;
				_sharedBuffer.ClearContents(new byte[(bitSize + 7) / 8]);
				_sharedBuffer.WriteTimestamp(DoubleProtocol.TimeMillis);
				BotCache.ForEach(structure => structure.SerializeState(_sharedBuffer));
				NetworkServer.UdpPayload = _sharedBuffer.Array;
			} else {
				NetworkServer.UdpPayload = null;
			}
		}

		private void ClientOnlyFixedUpdate() {
			CompleteStructure localStructure = BotCache.Get(NetworkUtils.LocalId);
			int toSimulate;
			long lastMillis;
			if (_lastClientUdpPacket != null) {
				_sharedBuffer.SetContents(_lastClientUdpPacket);
				_lastClientUdpPacket = null;
				ClientOnlyPacketReceived(localStructure, out toSimulate, out lastMillis);
				if (toSimulate == 0) {
					return;
				}
			} else if (_silentSkipFastForwardUntil != 0) {
				return; //don't simulate normal steps if skipped the last state update simulation
			} else {
				toSimulate = TimestepMillis;
				lastMillis = DoubleProtocol.TimeMillis - TimestepMillis;
			}

			while (_guessedInputs.Count > 0) {
				KeyValuePair<long, GuessedInput> guessed = _guessedInputs.First();
				if (guessed.Key >= lastMillis) {
					break;
				}

				_guessedInputs.Remove(guessed.Key);
				guessed.Value.RemainingDelay -= (int)(lastMillis - guessed.Key);
				if (guessed.Value.RemainingDelay > 0) {
					_guessedInputs.Remove(lastMillis);
					_guessedInputs.Add(lastMillis, guessed.Value);
				}
			}

			foreach (KeyValuePair<long, GuessedInput> guessed in _guessedInputs) {
				int delta = (int)(guessed.Key - lastMillis);
				if (delta > toSimulate) {
					break;
				} else if (delta == 0) {
					localStructure.UpdateInputOnly(guessed.Value.MovementInput, null);
				} else {
					Simulate(delta);
					localStructure.UpdateInputOnly(guessed.Value.MovementInput, null);
					toSimulate -= delta;
					lastMillis = guessed.Key;
				}
			}

			if (toSimulate != 0) {
				Simulate(toSimulate);
			}
		}

		private void ClientOnlyPacketReceived(CompleteStructure localStructure, out int toSimulate, out long lastMillis) {
			long currentMillis = DoubleProtocol.TimeMillis;
			lastMillis = _sharedBuffer.ReadTimestamp();
			toSimulate = (int)(currentMillis - lastMillis);
			while (_sharedBuffer.TotalBitsLeft >= BotState.SerializedBitsSize) {
				_tempBotState.Update(_sharedBuffer);
				BotCache.Get(_tempBotState.Id)?.UpdateWholeState(_tempBotState);
			}

			if (toSimulate <= 0) {
				toSimulate = 0;
				_silentSkipFastForwardUntil = 0;
			} else if (toSimulate >= 500) {
				if (currentMillis < _silentSkipFastForwardUntil) {
					Debug.LogWarning($"Skipping {toSimulate}ms of networking fast-forward simulation to avoid delays." +
						"Hiding this error for at most 100ms.");
					_silentSkipFastForwardUntil = currentMillis + 100;
				}
				toSimulate = 0;
			} else {
				_silentSkipFastForwardUntil = 0;
			}

			while (_guessedInputs.Count > 0) {
				KeyValuePair<long, GuessedInput> guessed = _guessedInputs.First();
				if (guessed.Value.MovementInput.Equals(localStructure.MovementInput)) {
					_guessedInputs.Remove(guessed.Key);
				} else {
					break;
				}
			}
		}

		private static void Simulate(int millis) {
			int fullSteps = millis / TimestepMillis;
			while (fullSteps-- > 0) {
				BotCache.ForEach(structure => structure.SimulatedPhysicsUpdate(1f));
				Physics.Simulate(TimestepSeconds);
			}

			int mod = millis % TimestepMillis;
			if (mod != 0) {
				float timestepMultiplier = (float)mod / TimestepMillis;
				BotCache.ForEach(structure => structure.SimulatedPhysicsUpdate(timestepMultiplier));
				Physics.Simulate(mod / 1000f);
			}
		}



		private class GuessedInput {
			public readonly Vector3 MovementInput;
			public int RemainingDelay = Mathf.RoundToInt(NetworkClient.UdpNetDelay + MaxNonNetDelayMillis) * 11 / 10;

			public GuessedInput(Vector3 movementMovementInput) {
				MovementInput = movementMovementInput;
			}
		}
	}
}
