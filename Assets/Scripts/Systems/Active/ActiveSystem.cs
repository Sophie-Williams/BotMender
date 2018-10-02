﻿using Blocks.Live;
using Structures;

namespace Systems.Active {
	/// <summary>
	/// A system which is activated by pressing a specific button.
	/// Only one system of this type can exists in a bot at once.
	/// </summary>
	public abstract class ActiveSystem : BotSystem {
		protected ActiveSystem(byte id, CompleteStructure structure, RealLiveBlock block) : base(id, structure, block) { }



		/// <summary>
		/// Activate the system.
		/// </summary>
		public abstract void Activate();
	}
}
