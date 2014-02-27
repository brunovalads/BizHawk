﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BizHawk.Emulation.Common;
using BizHawk.Common;

namespace BizHawk.Emulation.Cores.Nintendo.NES
{
	// we don't handle some possible connections of the expansion port that were never used

	public struct StrobeInfo
	{
		public int OUT0;
		public int OUT1;
		public int OUT2;
		public int OUT0old;
		public int OUT1old;
		public int OUT2old;
	}

	public interface IControllerDeck
	{
		void Strobe(StrobeInfo s, IController c);
		byte ReadA(IController c); // D0:D4
		byte ReadB(IController c); // D0:D4
		ControllerDefinition GetDefinition();
		void SyncState(Serializer ser);
	}

	public interface IFamicomExpansion
	{
		void Strobe(StrobeInfo s, IController c);
		byte ReadA(IController c); // only uses D1:D4
		byte ReadB(IController c); // only uses D1:D4
		ControllerDefinition GetDefinition();
		void SyncState(Serializer ser);
	}

	public interface INesPort
	{
		void Strobe(StrobeInfo s, IController c); // only uses OUT0
		byte Read(IController c); // only uses D0, D3, D4
		ControllerDefinition GetDefinition();
		void SyncState(Serializer ser);
	}

	public class NesDeck : IControllerDeck
	{
		INesPort Left;
		INesPort Right;
		ControlDefUnMerger LeftU;
		ControlDefUnMerger RightU;
		ControllerDefinition Definition;

		public NesDeck(INesPort Left, INesPort Right)
		{
			this.Left = Left;
			this.Right = Right;
			List<ControlDefUnMerger> cdum;
			Definition = ControllerDefMerger.GetMerged(new[] { Left.GetDefinition(), Right.GetDefinition() }, out cdum);
			LeftU = cdum[0];
			RightU = cdum[1];
		}

		public void Strobe(StrobeInfo s, IController c)
		{
			Left.Strobe(s, LeftU.UnMerge(c));
			Right.Strobe(s, RightU.UnMerge(c));
		}

		public byte ReadA(IController c)
		{
			return (byte)(Left.Read(LeftU.UnMerge(c)) & 0x13);
		}

		public byte ReadB(IController c)
		{
			return (byte)(Right.Read(RightU.UnMerge(c)) & 0x13);
		}

		public ControllerDefinition GetDefinition()
		{
			return Definition;
		}

		public void SyncState(Serializer ser)
		{
			ser.BeginSection("Left");
			Left.SyncState(ser);
			ser.EndSection();
			ser.BeginSection("Right");
			Right.SyncState(ser);
			ser.EndSection();
		}
	}

	public class UnpluggedNES : INesPort
	{
		public void Strobe(StrobeInfo s, IController c)
		{
		}

		public byte Read(IController c)
		{
			return 0;
		}

		public ControllerDefinition GetDefinition()
		{
			return new ControllerDefinition();
		}

		public void SyncState(Serializer ser)
		{
		}
	}

	public class ControllerNES : INesPort
	{
		int shiftidx = 0;
		bool resetting = false;

		static string[] Buttons =
		{
			"0A", "0B", "0Select", "0Start", "0Up", "0Down", "0Left", "0Right"
		};
		static string[] FamicomP2Buttons =
		{
			"0A", "0B", "0Up", "0Down", "0Left", "0Right"
		};
		bool FamicomP2Hack;

		ControllerDefinition Definition;
		
		public ControllerNES()
		{
			Definition = new ControllerDefinition { BoolButtons = new List<string>(Buttons) };
		}

		public ControllerNES(bool famicomP2)
		{
			if (famicomP2)
				Definition = new ControllerDefinition { BoolButtons = new List<string>(FamicomP2Buttons) };
			else
				Definition = new ControllerDefinition { BoolButtons = new List<string>(Buttons) };
			FamicomP2Hack = famicomP2;
		}

		public void Strobe(StrobeInfo s, IController c)
		{
			resetting = s.OUT0 != 0;
			if (resetting)
				shiftidx = 0;
		}

		public byte Read(IController c)
		{
			byte ret = 1;
			if (shiftidx < 8)
			{
				if (!FamicomP2Hack || shiftidx < 2 || shiftidx > 3)
					if (!c[Buttons[shiftidx]])
						ret = 0;
				if (!resetting)
					shiftidx++;
			}
			return ret;
		}

		public ControllerDefinition GetDefinition()
		{
			return Definition;
		}

		public void SyncState(Serializer ser)
		{
			ser.Sync("shiftidx", ref shiftidx);
			ser.Sync("restting", ref resetting);
		}
	}

	public class ArkanoidNES : INesPort
	{
		int shiftidx = 0;
		bool resetting = false;
		byte latchedvalue = 0x54;

		static ControllerDefinition Definition = new ControllerDefinition
		{
			BoolButtons = { "0Fire" },
			FloatControls = { "0Paddle" },
			FloatRanges = { new[] { 0.0f, 80.0f, 160.0f } }
		};

		public void Strobe(StrobeInfo s, IController c)
		{
			resetting = s.OUT0 != 0;
			if (resetting)
				shiftidx = 0;
			if (s.OUT0 > s.OUT0old)
				latchedvalue = (byte)(0x54 + (int)c.GetFloat("0Paddle"));
		}

		public byte Read(IController c)
		{
			byte ret = c["0Fire"] ? (byte)0x08 : (byte)0x00;
			if (resetting)
				return ret;

			byte value = latchedvalue;
			value >>= (3 - shiftidx);
			ret |= (byte)(value & 0x10);
			shiftidx++;
			return ret;
		}

		public ControllerDefinition GetDefinition()
		{
			return Definition;
		}

		public void SyncState(Serializer ser)
		{
			ser.Sync("shiftidx", ref shiftidx);
			ser.Sync("restting", ref resetting);
			ser.Sync("latchedvalue", ref latchedvalue);
		}
	}

	public class FourScore : INesPort
	{
		// fourscore is actually one two port thing
		// we emulate it as two separate halves
		// each one behaves slightly differently
		public bool RightPort = false;

		static string[] Buttons =
		{
			"0A", "0B", "0Select", "0Start", "0Up", "0Down", "0Left", "0Right",
			"1A", "1B", "1Select", "1Start", "1Up", "1Down", "1Left", "1Right",
		};
		static ControllerDefinition Definition = new ControllerDefinition { BoolButtons = new List<string>(Buttons) };

		int shiftidx = 0;
		bool resetting = false;

		public void Strobe(StrobeInfo s, IController c)
		{
			resetting = s.OUT0 != 0;
			if (resetting)
				shiftidx = 0;
		}

		public byte Read(IController c)
		{
			byte ret = 1;
			if (shiftidx < 16)
			{
				if (!c[Buttons[shiftidx]])
					ret = 0;
				if (!resetting)
					shiftidx++;
			}
			else if (shiftidx < 24)
			{
				if (shiftidx != (RightPort ? 18 : 19))
					ret = 0;
				if (!resetting)
					shiftidx++;
			}
			return ret;
		}

		public ControllerDefinition GetDefinition()
		{
			return Definition;
		}

		public void SyncState(Serializer ser)
		{
			ser.Sync("shiftidx", ref shiftidx);
			ser.Sync("restting", ref resetting);
		}
	}

	public class PowerPad : INesPort
	{
		static string[] D3Buttons = { "0PP2", "0PP1", "0PP5", "0PP9", "0PP6", "0PP10", "0PP11", "0PP7" };
		static string[] D4Buttons = { "0PP4", "0PP3", "0PP12", "0PP8" };
		static ControllerDefinition Definition = new ControllerDefinition { BoolButtons = new List<string>(D3Buttons.Concat(D4Buttons)) };

		int shiftidx = 0;
		bool resetting = false;

		public void Strobe(StrobeInfo s, IController c)
		{
			resetting = s.OUT0 != 0;
			if (resetting)
				shiftidx = 0;
		}

		public byte Read(IController c)
		{
			int d3 = 0x08;
			if (shiftidx < D3Buttons.Length && !c[D3Buttons[shiftidx]])
				d3 = 0;
			int d4 = 0x10;
			if (shiftidx < D4Buttons.Length && !c[D4Buttons[shiftidx]])
				d4 = 0;
			if (shiftidx < 8 && !resetting)
				shiftidx++;

			return (byte)(d3 | d4);
		}

		public ControllerDefinition GetDefinition()
		{
			return Definition;
		}

		public void SyncState(Serializer ser)
		{
			ser.Sync("shiftidx", ref shiftidx);
			ser.Sync("restting", ref resetting);
		}
	}

	public class Zapper : INesPort, IFamicomExpansion
	{
		public Func<int, int, bool> PPUCallback;

		static ControllerDefinition Definition = new ControllerDefinition
		{
			BoolButtons = { "0Fire" },
			FloatControls = { "0Zapper X", "0Zapper Y"},
			FloatRanges = { new[] { 0.0f, 128.0f, 255.0f }, new[] { 0.0f, 120.0f, 239.0f } }
		};


		public void Strobe(StrobeInfo s, IController c)
		{
		}

		public byte Read(IController c)
		{
			byte ret = 0;
			if (c["0Fire"])
				ret |= 0x08;
			if (PPUCallback((int)c.GetFloat("0Zapper X"), (int)c.GetFloat("0Zapper Y")))
				ret |= 0x10;
			return ret;
		}

		public ControllerDefinition GetDefinition()
		{
			return Definition;
		}

		public void SyncState(Serializer ser)
		{
		}

		// famicom expansion hookups
		public byte ReadA(IController c)
		{
			return 0;
		}

		public byte ReadB(IController c)
		{
			return Read(c);
		}
	}

	public class FamicomDeck : IControllerDeck
	{
		// two NES controllers are maintained internally
		INesPort Player1 = new ControllerNES(false);
		INesPort Player2 = new ControllerNES(true);
		IFamicomExpansion Player3;

		ControlDefUnMerger Player1U;
		ControlDefUnMerger Player2U;
		ControlDefUnMerger Player3U;

		ControllerDefinition Definition;

		public FamicomDeck(IFamicomExpansion ExpSlot)
		{
			Player3 = ExpSlot;
			List<ControlDefUnMerger> cdum;
			Definition = ControllerDefMerger.GetMerged(
				new[] { Player1.GetDefinition(), Player2.GetDefinition(), Player3.GetDefinition() }, out cdum);
			Definition.BoolButtons.Add("P2 Microphone");
			Player1U = cdum[0];
			Player2U = cdum[1];
			Player3U = cdum[2];
		}

		public void Strobe(StrobeInfo s, IController c)
		{
			Player1.Strobe(s, Player1U.UnMerge(c));
			Player2.Strobe(s, Player2U.UnMerge(c));
			Player3.Strobe(s, Player3U.UnMerge(c));
		}

		public byte ReadA(IController c)
		{
			byte ret = 0;
			ret |= (byte)(Player1.Read(Player1U.UnMerge(c)) & 1);
			ret |= (byte)(Player3.ReadA(Player3U.UnMerge(c)) & 2);
			if (c["P2 Microphone"])
				ret |= 4;
			return ret;
		}

		public byte ReadB(IController c)
		{
			byte ret = 0;
			ret |= (byte)(Player2.Read(Player2U.UnMerge(c)) & 1);
			ret |= (byte)(Player3.ReadB(Player3U.UnMerge(c)) & 30);
			return ret;
		}

		public ControllerDefinition GetDefinition()
		{
			return Definition;
		}

		public void SyncState(Serializer ser)
		{
			ser.BeginSection("Left");
			Player1.SyncState(ser);
			ser.EndSection();
			ser.BeginSection("Right");
			Player2.SyncState(ser);
			ser.EndSection();
			ser.BeginSection("Expansion");
			Player3.SyncState(ser);
			ser.EndSection();
		}
	}

	public class ArkanoidFam : IFamicomExpansion
	{
		int shiftidx = 0;
		bool resetting = false;
		byte latchedvalue = 0x54;

		static ControllerDefinition Definition = new ControllerDefinition
		{
			BoolButtons = { "0Fire" },
			FloatControls = { "0Paddle" },
			FloatRanges = { new[] { 0.0f, 80.0f, 160.0f } }
		};

		public void Strobe(StrobeInfo s, IController c)
		{
			resetting = s.OUT0 != 0;
			if (resetting)
				shiftidx = 0;
			if (s.OUT0 > s.OUT0old)
				latchedvalue = (byte)(0x54 + (int)c.GetFloat("0Paddle"));
		}

		public byte ReadA(IController c)
		{
			return c["0Fire"] ? (byte)0x02 : (byte)0x00;
		}

		public byte ReadB(IController c)
		{
			byte ret = 0;
			if (resetting)
				return ret;

			byte value = latchedvalue;
			value >>= (6 - shiftidx);
			ret |= (byte)(value & 0x02);
			shiftidx++;
			return ret;
		}

		public ControllerDefinition GetDefinition()
		{
			return Definition;
		}

		public void SyncState(Serializer ser)
		{
			ser.Sync("shiftidx", ref shiftidx);
			ser.Sync("restting", ref resetting);
			ser.Sync("latchedvalue", ref latchedvalue);
		}
	}

	public class FamilyBasicKeyboard: IFamicomExpansion
	{
		#region buttonlookup
		static string[] Buttons =
		{
			"0]",
			"0[",
			"0RETURN",
			"0F8",
			"0STOP",
			"0¥",
			"0RSHIFT",
			"0カナ",

			"0;",
			"0:",
			"0@",
			"0F7",
			"0^",
			"0-",
			"0/",
			"0_",

			"0K",
			"0L",
			"0O",
			"0F6",
			"00",
			"0P",
			"0,",
			"0.",

			"0J",
			"0U",
			"0I",
			"0F5",
			"08",
			"09",
			"0N",
			"0M",

			"0H",
			"0G",
			"0Y",
			"0F4",
			"06",
			"07",
			"0V",
			"0B",

			"0D",
			"0R",
			"0T",
			"0F3",
			"04",
			"05",
			"0C",
			"0F",

			"0A",
			"0S",
			"0W",
			"0F2",
			"03",
			"0E",
			"0Z",
			"0X",

			"0CTR",
			"0Q",
			"0ESC",
			"0F1",
			"02",
			"01",
			"0GRPH",
			"0LSHIFT",

			"0LEFT",
			"0RIGHT",
			"0UP",
			"0CLR",
			"0INS",
			"0DEL",
			"0SPACE",
			"0DOWN",

		};
		#endregion

		static ControllerDefinition Definition = new ControllerDefinition { BoolButtons = new List<string>(Buttons) };

		bool active;
		int columnselect;
		int row;

		public void Strobe(StrobeInfo s, IController c)
		{
			active = s.OUT2 != 0;
			columnselect = s.OUT1;
			if (s.OUT1 > s.OUT1old)
			{
				row++;
				if (row == 10)
					row = 0;
			}
			if (s.OUT0 != 0)
				row = 0;
		}

		public byte ReadA(IController c)
		{
			return 0;
		}

		public byte ReadB(IController c)
		{
			if (!active)
				return 0;
			if (row == 9) // empty last row
				return 0;
			int idx = row * 8 + columnselect * 4;

			byte ret = 0;

			if (c[Buttons[idx]]) ret |= 16;
			if (c[Buttons[idx + 1]]) ret |= 8;
			if (c[Buttons[idx + 2]]) ret |= 4;
			if (c[Buttons[idx + 3]]) ret |= 2;

			// nothing is clocked here
			return ret;
		}

		public ControllerDefinition GetDefinition()
		{
			return Definition;
		}

		public void SyncState(Serializer ser)
		{
			ser.Sync("active", ref active);
			ser.Sync("columnselect", ref columnselect);
			ser.Sync("row", ref row);
		}
	}

	public class Famicom4P : IFamicomExpansion
	{
		static string[] Buttons =
		{
			"0A", "0B", "0Select", "0Start", "0Up", "0Down", "0Left", "0Right",
			"1A", "1B", "1Select", "1Start", "1Up", "1Down", "1Left", "1Right",
		};
		static ControllerDefinition Definition = new ControllerDefinition { BoolButtons = new List<string>(Buttons) };

		int shiftidx1 = 0;
		int shiftidx2 = 0;
		bool resetting = false;

		public void Strobe(StrobeInfo s, IController c)
		{
			resetting = s.OUT0 != 0;
			if (resetting)
			{
				shiftidx1 = 0;
				shiftidx2 = 0;
			}
		}

		public byte ReadA(IController c)
		{
			byte ret = 2;
			if (shiftidx1 < 8)
			{
				if (!c[Buttons[shiftidx1]])
						ret = 0;
				if (!resetting)
					shiftidx1++;
			}
			return ret;
		}

		public byte ReadB(IController c)
		{
			byte ret = 2;
			if (shiftidx2 < 8)
			{
				if (!c[Buttons[shiftidx2 + 8]])
					ret = 0;
				if (!resetting)
					shiftidx2++;
			}
			return ret;
		}

		public ControllerDefinition GetDefinition()
		{
			return Definition;
		}

		public void SyncState(Serializer ser)
		{
			ser.Sync("shiftidx1", ref shiftidx1);
			ser.Sync("shiftidx1", ref shiftidx1);
			ser.Sync("resetting", ref resetting);
		}
	}

	public class OekaKids : IFamicomExpansion
	{
		static ControllerDefinition Definition = new ControllerDefinition
		{
			BoolButtons = { "0Click", "0Touch" },
			FloatControls = { "0Pen X", "0Pen Y" },
			FloatRanges = { new[] { 0.0f, 128.0f, 255.0f }, new[] { 0.0f, 120.0f, 239.0f } }
		};

		bool resetting;
		int shiftidx;
		int latchedvalue = 0;

		public void Strobe(StrobeInfo s, IController c)
		{
			resetting = s.OUT0 == 0;
			if (s.OUT0 < s.OUT0old) // H->L: latch
			{
				int x = (int)c.GetFloat("0Pen X");
				int y = (int)c.GetFloat("0Pen Y");
				// http://forums.nesdev.com/viewtopic.php?p=19454#19454
				x = (x + 8) * 240 / 256;
				y = (y - 14) * 256 / 240;
				x &= 255;
				y &= 255;
				latchedvalue = x << 10 | y << 2;
				if (c["0Touch"])
					latchedvalue |= 2;
				if (c["0Click"])
					latchedvalue |= 1;
			}
			if (s.OUT0 > s.OUT0old) // L->H: reset shift
				shiftidx = 0;
			if (s.OUT1 > s.OUT1old) // L->H: increment shift
				shiftidx++;
		}

		public byte ReadA(IController c)
		{
			return 0;
		}

		public byte ReadB(IController c)
		{
			byte ret = (byte)(resetting ? 2 : 0);
			if (resetting)
				return ret;

			// the shiftidx = 0 read is one off the end
			int bit = latchedvalue >> (16 - shiftidx);
			bit &= 4;
			bit ^= 4; // inverted data
			ret |= (byte)(bit);
			return ret;
		}

		public ControllerDefinition GetDefinition()
		{
			return Definition;
		}

		public void SyncState(Serializer ser)
		{
			ser.Sync("resetting", ref resetting);
			ser.Sync("shiftidx", ref shiftidx);
			ser.Sync("latchedvalue", ref latchedvalue);
		}
	}

	public class UnpluggedFam : IFamicomExpansion
	{
		public void Strobe(StrobeInfo s, IController c)
		{
		}

		public byte ReadA(IController c)
		{
			return 0;
		}

		public byte ReadB(IController c)
		{
			return 0;
		}

		public ControllerDefinition GetDefinition()
		{
			return new ControllerDefinition();
		}

		public void SyncState(Serializer ser)
		{
		}
	}

	public class ControlDefUnMerger
	{
		Dictionary<string, string> Remaps;

		public ControlDefUnMerger(Dictionary<string, string> Remaps)
		{
			this.Remaps = Remaps;
		}

		private class DummyController : IController
		{
			public DummyController() { Type = new ControllerDefinition { Name = "Dummy" }; }
			public ControllerDefinition Type { get; private set; }

			public Dictionary<string, bool> Bools = new Dictionary<string, bool>();
			public Dictionary<string, float> Floats = new Dictionary<string, float>();

			public bool this[string button] { get { return Bools[button]; } }
			public bool IsPressed(string button) { return Bools[button]; }

			public float GetFloat(string name) { return Floats[name]; }
		}

		public IController UnMerge(IController c)
		{
			string r;
			var ret = new DummyController();

			var t = c.Type;

			foreach (string s in t.BoolButtons)
			{
				Remaps.TryGetValue(s, out r);
				if (r != null)
				{
					ret.Type.BoolButtons.Add(r);
					ret.Bools[r] = c[s];
				}
			}
			for (int i = 0; i < t.FloatControls.Count; i++)
			{
				Remaps.TryGetValue(t.FloatControls[i], out r);
				if (r != null)
				{
					ret.Type.FloatControls.Add(r);
					ret.Type.FloatRanges.Add(t.FloatRanges[i]);
					ret.Floats[r] = c.GetFloat(t.FloatControls[i]);
				}
			}
			return ret;
		}

	}

	public static class ControllerDefMerger
	{
		private static string Allocate(string input, ref int plr, ref int plrnext)
		{
			int offset = int.Parse(input.Substring(0, 1));
			int currplr = plr + offset;
			if (currplr >= plrnext)
				plrnext = currplr + 1;
			return string.Format("P{0} {1}", currplr, input.Substring(1));
		}
			
		/// <summary>
		/// handles all player number merging
		/// </summary>
		/// <param name="Controllers"></param>
		/// <returns></returns>
		public static ControllerDefinition GetMerged(IEnumerable<ControllerDefinition> Controllers, out List<ControlDefUnMerger> Unmergers)
		{
			ControllerDefinition ret = new ControllerDefinition();
			Unmergers = new List<ControlDefUnMerger>();
			int plr = 1;
			int plrnext = 1;
			foreach (var def in Controllers)
			{
				Dictionary<string, string> remaps = new Dictionary<string, string>();

				foreach (string s in def.BoolButtons)
				{
					string r = Allocate(s, ref plr, ref plrnext);
					ret.BoolButtons.Add(r);
					remaps[s] = r;
				}
				foreach (string s in def.FloatControls)
				{
					string r = Allocate(s, ref plr, ref plrnext);
					ret.FloatControls.Add(r);
					remaps[s] = r;
				}
				ret.FloatRanges.AddRange(def.FloatRanges);
				plr = plrnext;
				Unmergers.Add(new ControlDefUnMerger(remaps));
			}
			return ret;
		}
	}
}
