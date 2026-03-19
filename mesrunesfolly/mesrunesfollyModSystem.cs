using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MesrunesFolly
{
	public class MainModSystem : ModSystem
	{
		//static int? FlagIndex;

		static ICoreAPI? Api;

		[ThreadStatic]
		static ICachingBlockAccessor? _blockAcessor;
		static ICachingBlockAccessor BlockAccessor
		{
			get
			{
				if (Api == null) throw new Exception("Api is null... Cannot get BlockAccessors to find rooms x_x");

				if (_blockAcessor == null)
				{
					_blockAcessor = Api.World.GetCachingBlockAccessor(false, false);
					_blockAccessors[Environment.CurrentManagedThreadId] = _blockAcessor;
				}

				return _blockAcessor;
			}

		}
		static readonly ConcurrentDictionary<int, ICachingBlockAccessor> _blockAccessors = new(4, 16);

		public override void Dispose()
		{
			foreach (var accessor in _blockAccessors.Values)
			{
				accessor.Dispose();
			}
			_blockAccessors.Clear();
		}

		[HarmonyPatch(typeof(RoomRegistry), "FindRoomForPosition")]
		public static class CellarPatch
		{
			static readonly int ArraySize;
			static readonly int MaxSize;
			static readonly int MaxRoomSize;
			static readonly int MaxCellarSize;
			static readonly int MaxVolume;
			static readonly int AltMaxCellarSize;
			static readonly int[] CurrentVisited;
			static readonly int[] SkyLightChecked;
			static readonly bool OnlyVolumeForCellar;

			static readonly Stopwatch stopwatch = new();

			static CellarPatch()
			{
				MaxRoomSize = Config.MaxRoomSize;
				MaxCellarSize = Config.MaxCellarSize;
				AltMaxCellarSize = Config.AlternateMaxCellarSize;
				MaxVolume = Config.MaxCellarVolume;
				OnlyVolumeForCellar = Config.OnlyVolumeForCellar;

				MaxSize = MaxRoomSize << 1;
				var arraySize = MaxSize | 1;
				ArraySize = arraySize;

				CurrentVisited = new int[arraySize * arraySize * arraySize];
				SkyLightChecked = new int[arraySize * arraySize];
			}

			static int iteration = 0;

			public static bool Prefix(ref Room __result, BlockPos pos)
			{
				stopwatch.Start();

				QueueOfInt bfsQueue = new();

				bfsQueue.Enqueue(MaxRoomSize << 16 | MaxRoomSize << 8 | MaxRoomSize);

				int visitedIndex = CurrentVisited.Length >> 1;

				int iteration = ++CellarPatch.iteration;
				CurrentVisited[visitedIndex] = iteration;

				int coolingWallCount = 0;
				int nonCoolingWallCount = 0;

				int skyLightCount = 0;
				int nonSkyLightCount = 0;
				int exitCount = 0;

				BlockAccessor.Begin();

				bool allChunksLoaded = true;

				int minx = MaxRoomSize, miny = MaxRoomSize, minz = MaxRoomSize, maxx = MaxRoomSize, maxy = MaxRoomSize, maxz = MaxRoomSize;
				int posX = pos.X - MaxRoomSize;
				int posY = pos.Y - MaxRoomSize;
				int posZ = pos.Z - MaxRoomSize;
				BlockPos npos = new(pos.dimension);
				BlockPos bpos = new(pos.dimension);
				int dx, dy, dz;

				while (bfsQueue.Count > 0)
				{
					int compressedPos = bfsQueue.Dequeue();
					dx = compressedPos >> 16;
					dy = (compressedPos >> 8) & 0x1f;
					dz = compressedPos & 0x1f;
					npos.Set(posX + dx, posY + dy, posZ + dz);
					bpos.Set(npos);

					if (dx < minx) minx = dx;
					else if (dx > maxx) maxx = dx;

					if (dy < miny) miny = dy;
					else if (dy > maxy) maxy = dy;

					if (dz < minz) minz = dz;
					else if (dz > maxz) maxz = dz;

					Block bBlock = BlockAccessor.GetBlock(bpos);

					foreach (BlockFacing facing in BlockFacing.ALLFACES)
					{
						facing.IterateThruFacingOffsets(npos);
						int heatRetention = bBlock.GetRetention(bpos, facing, EnumRetentionType.Heat);

						if (bBlock.Id != 0 && heatRetention != 0)
						{
							if (heatRetention < 0) coolingWallCount -= heatRetention;
							else nonCoolingWallCount += heatRetention;

							continue;
						}

						if (!BlockAccessor.IsValidPos(npos))
						{
							nonCoolingWallCount++;
							continue;
						}

						Block nBlock = BlockAccessor.GetBlock(npos);
						allChunksLoaded &= BlockAccessor.LastChunkLoaded;
						heatRetention = nBlock.GetRetention(npos, facing.Opposite, EnumRetentionType.Heat);

						if (heatRetention != 0)
						{
							if (heatRetention < 0) coolingWallCount -= heatRetention;
							else nonCoolingWallCount += heatRetention;

							continue;
						}

						dx = npos.X - posX;
						dy = npos.Y - posY;
						dz = npos.Z - posZ;

						bool outsideCube = false;
						switch (facing.Index)
						{
							case 0: // North
								if (dz < minz) outsideCube = dz < 0 || maxz - minz + 1 >= MaxRoomSize;
								break;
							case 1: // East
								if (dx > maxx) outsideCube = dx > MaxSize || maxx - minx + 1 >= MaxRoomSize;
								break;
							case 2: // South
								if (dz > maxz) outsideCube = dz > MaxSize || maxz - minz + 1 >= MaxRoomSize;
								break;
							case 3: // West
								if (dx < minx) outsideCube = dx < 0 || maxx - minx + 1 >= MaxRoomSize;
								break;
							case 4: // Up
								if (dy > maxy) outsideCube = dy > MaxSize || maxy - miny + 1 >= MaxRoomSize;
								break;
							case 5: // Down
								if (dy < miny) outsideCube = dy < 0 || maxy - miny + 1 >= MaxRoomSize;
								break;
						}
						if (outsideCube)
						{
							exitCount++;
							continue;
						}

						visitedIndex = (dx * ArraySize + dy) * ArraySize + dz;
						if (CurrentVisited[visitedIndex] == iteration) continue;
						CurrentVisited[visitedIndex] = iteration;

						int skyLightIndex = dx * ArraySize + dz;
						if (SkyLightChecked[skyLightIndex] < iteration)
						{
							SkyLightChecked[skyLightIndex] = iteration;
							int light = BlockAccessor.GetLightLevel(npos, EnumLightLevelType.OnlySunLight);

							if (light >= Api?.World.SunBrightness - 1)
							{
								skyLightCount++;
							}
							else
							{
								nonSkyLightCount++;
							}
						}

						bfsQueue.Enqueue(dx << 16 | dy << 8 | dz);
					}
				}

				int sizex = maxx - minx + 1;
				int sizey = maxy - miny + 1;
				int sizez = maxz - minz + 1;

				byte[] posInRoom = new byte[(sizex * sizey * sizez + 7) / 8];

				int volumeCount = 0;
				for (dx = 0; dx < sizex; dx++)
				{
					for (dy = 0; dy < sizey; dy++)
					{
						visitedIndex = ((dx + minx) * ArraySize + (dy + miny)) * ArraySize + minz;
						for (dz = 0; dz < sizez; dz++)
						{
							if (CurrentVisited[visitedIndex + dz] == iteration)
							{
								int index = (dy * sizez + dz) * sizex + dx;

								posInRoom[index / 8] = (byte)(posInRoom[index / 8] | (1 << (index % 8)));
								volumeCount++;
							}
						}
					}
				}

				bool isCellar;

				if (OnlyVolumeForCellar)
				{
					isCellar = volumeCount <= MaxVolume;
				}
				else
				{
					isCellar = sizex <= MaxCellarSize && sizey <= MaxCellarSize && sizez <= MaxCellarSize;
					if (!isCellar && volumeCount <= MaxVolume)
					{
						isCellar = sizex <= AltMaxCellarSize && sizey <= MaxCellarSize && sizez <= MaxCellarSize
							|| sizex <= MaxCellarSize && sizey <= AltMaxCellarSize && sizez <= MaxCellarSize
							|| sizex <= MaxCellarSize && sizey <= MaxCellarSize && sizez <= AltMaxCellarSize;
					}
				}

				__result = new Room()
				{
					CoolingWallCount = coolingWallCount,
					NonCoolingWallCount = nonCoolingWallCount,
					SkylightCount = skyLightCount,
					NonSkylightCount = nonSkyLightCount,
					ExitCount = exitCount,
					AnyChunkUnloaded = allChunksLoaded ? 0 : 1,
					Location = new Cuboidi(posX + minx, posY + miny, posZ + minz, posX + maxx, posY + maxy, posZ + maxz),
					PosInRoom = posInRoom,
					IsSmallRoom = isCellar && exitCount == 0
				};

				return false;
			}

			public static void Postfix(RoomRegistry __instance)
			{
				stopwatch.Stop();

				var elapsed = stopwatch.Elapsed;
				if (elapsed != TimeSpan.Zero)
				{
					__instance.Mod.Logger.Warning($"{stopwatch.Elapsed.TotalMilliseconds}");
					stopwatch.Reset();
				}
			}

			//        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			//        {
			//            List<CodeInstruction> codes = [.. instructions];

			//            var flagIndex = codes.FindIndex(c => c.IsStloc() && c.LocalIndex() == 28);
			//            FlagIndex ??= flagIndex;

			//if (flagIndex == FlagIndex)
			//            {
			//                var comparisonIndex = codes.FindLastIndex(flagIndex, c => c.opcode == OpCodes.Cgt);
			//                var start = codes.FindLastIndex(comparisonIndex, c => c.IsLdloc() && c.LocalIndex() == 23);

			//                var volumeIndex = codes.FindIndex(flagIndex, c => c.IsLdloc() && c.LocalIndex() == 27);
			//                var end = codes.FindIndex(volumeIndex, c => c.IsStloc() && c.LocalIndex() == 28);

			//                var volumeCodes = codes.GetRange(volumeIndex, 2);
			//                var comparisonCodes = codes.GetRange(comparisonIndex, 3);

			//                codes.RemoveRange(start, end - start);
			//                codes.InsertRange(start, comparisonCodes);
			//                codes.InsertRange(start, volumeCodes);
			//            }

			//            return [.. codes];
			//        }
		}

		const string CONFIGFILE = "mesrunesFolly.json";
		static MainConfig? _config;
		static MainConfig Config
		{
			get
			{
				if (_config == null)
				{
					_config = new();

					Api?.StoreModConfig(_config, CONFIGFILE);
				}
				return _config;
			}
			set { _config = value; }
		}

		public override void Start(ICoreAPI api)
		{
			Api = api;

			try
			{
				Config = api.LoadModConfig<MainConfig>(CONFIGFILE);

				if (_config == null)
				{
					Mod.Logger.Notification("Config file not found : falling back to default settings");
				}

			}
			catch (Exception e)
			{
				Mod.Logger.Error($"File parsing failed due to : {e.Message}");
				Mod.Logger.Warning("Falling back to default settings");

				_config = new();
			}
			finally
			{
				var harmony = new Harmony("mesrunesfolly");
				harmony.PatchAll();
			}
		}
	}

	class MainConfig
	{
		private int? _maxRoomSize;
		public int MaxRoomSize { get { return _maxRoomSize ?? 14; }
			set
			{
				if (value < 2) value = 2;
				else if (value > 32) value = 32;

				if (_maxCellarSize > value) _maxCellarSize = value;
				if (_alternateMaxCellarSize > value) _alternateMaxCellarSize = value;

				_maxRoomSize = value;
			}
		}

		private int? _maxCellarSize;
		public int MaxCellarSize { get { return _maxCellarSize ?? 7; }
			set
			{
				if (value < 2) value = 2;
				else if (_maxRoomSize != null && value > _maxRoomSize) value = MaxRoomSize;
				
				_maxCellarSize = value;
			}
		}

		private int? _alternateMaxCellarSize;
		public int AlternateMaxCellarSize { get { return _alternateMaxCellarSize ?? 9; }
			set
			{
				if (_maxRoomSize != null && value > _maxRoomSize) value = MaxRoomSize;

				_alternateMaxCellarSize = value;
			}
		}

		public bool OnlyVolumeForCellar { get; set; } = false;
		public int MaxCellarVolume { get; set; } = 150;
	}
}