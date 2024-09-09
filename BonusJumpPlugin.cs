using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour.HookGen;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EntityStates;
using RoR2;
using RoR2.ContentManagement;

using System.Security;
using System.Security.Permissions;

[module: UnverifiableCode]
#pragma warning disable CS0618 // Type or member is obsolete
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618 // Type or member is obsolete

namespace TPDespair.BonusJump
{
	[BepInPlugin(ModGuid, ModName, ModVer)]

	public class BonusJumpPlugin : BaseUnityPlugin
	{
		public const string ModVer = "1.1.0";
		public const string ModName = "BonusJump";
		public const string ModGuid = "com.TPDespair.BonusJump";

		public static ConfigFile configFile;
		public static ManualLogSource logSource;

		public static AssetBundle Assets;

		public static Dictionary<string, string> LangTokens = new Dictionary<string, string>();

		private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

		public static BuffDef availableBuff;
		public static BuffDef waitingBuff;
		public static BuffDef cooldownBuff;

		public static ItemIndex RainbowCloudItem = ItemIndex.None;

		public static ConfigEntry<float> JumpCooldown { get; set; }
		public static ConfigEntry<float> GroundThreshold { get; set; }
		public static ConfigEntry<float> CloudCompat { get; set; }



		public void Awake()
		{
			RoR2Application.isModded = true;
			NetworkModCompatibilityHelper.networkModList = NetworkModCompatibilityHelper.networkModList.Append(ModGuid + ":" + ModVer);

			configFile = Config;
			logSource = Logger;

			SetupConfig();

			LanguageOverride();

			if (CloudCompat.Value > 0f)
			{
				float value = CloudCompat.Value * 100f;
				RegisterToken("ITEM_MYSTICSBADITEMS_RAINBOWTRAIL_DESC", "Gain <style=cIsUtility>" + value + "%</style> <style=cStack>(+" + value + "% per stack)</style> <style=cIsUtility>cooldown reduction</style> towards <style=cIsUtility>bonus jump</style>.\nLeave a <link=\"MysticsBadItemsRainbowWavy\">rainbow trail</link> behind.");
				RegisterToken("ITEM_MYSTICSBADITEMS_RAINBOWTRAIL_PICKUP", "Gain bonus jump cooldown recovery and leave a <link=\"MysticsBadItemsRainbowWavy\">rainbow trail</link> behind.");
			}

			ContentManager.collectContentPackProviders += ContentManager_collectContentPackProviders;

			JumpCountHook();

			IndicatorBuffDisplayHook();

			CharacterBody.onBodyInventoryChangedGlobal += HandleItemBehavior;
			HandleCleanseHook();
			UsageRequestedHook();

			RoR2Application.onLoad += LateSetup;

			//On.RoR2.Networking.NetworkManagerSystemSteam.OnClientConnect += (s, u, t) => { };
		}



		private static void LateSetup()
		{
			AllocateBehaviorBuffs();

			if (PluginLoaded("com.themysticsword.mysticsbaditems"))
			{
				RainbowCloudItem = FindItemIndex("MysticsBadItems_RainbowTrail");
			}

			PaladinCompat();
		}

		private static ItemIndex FindItemIndex(string itemName)
		{
			ItemIndex itemIndex = ItemCatalog.FindItemIndex(itemName);
			if (itemIndex == ItemIndex.None)
			{
				LogWarn("Could not find ItemIndex for : " + itemName);
			}
			return itemIndex;
		}



		private static void SetupConfig()
		{
			JumpCooldown = configFile.Bind(
				"General", "JumpCooldown", 10f,
				"Cooldown of bonus jump. Cooldown only recovers while grounded."
			);
			GroundThreshold = configFile.Bind(
				"General", "GroundThreshold", 0.25f,
				"Time after not being grounded to still be counted as grounded for cooldown recovery."
			);
			CloudCompat = configFile.Bind(
				"General", "CloudCompat", 0.25f,
				"Bonus jump cooldown reduction per Cloud Nine. Set to 0 to disable."
			);
		}



		private void ContentManager_collectContentPackProviders(ContentManager.AddContentPackProviderDelegate addContentPackProvider)
		{
			addContentPackProvider(new BonusJumpContent());
		}

		internal static void LoadAssets()
		{
			using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TPDespair.BonusJump.bonusjumpbundle"))
			{
				Assets = AssetBundle.LoadFromStream(stream);
			}
		}



		private static void LanguageOverride()
		{
			On.RoR2.Language.TokenIsRegistered += (orig, self, token) =>
			{
				if (token != null)
				{
					if (LangTokens.ContainsKey(token)) return true;
				}

				return orig(self, token);
			};

			On.RoR2.Language.GetString_string += (orig, token) =>
			{
				if (token != null)
				{
					if (LangTokens.ContainsKey(token)) return LangTokens[token];
				}

				return orig(token);
			};
		}

		public static void RegisterToken(string token, string text)
		{
			if (!LangTokens.ContainsKey(token)) LangTokens.Add(token, text);
			else LangTokens[token] = text;
		}



		internal static void LogWarn(object data)
		{
			logSource.LogWarning(data);
		}

		internal static bool PluginLoaded(string key)
		{
			return BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(key);
		}



		private static void JumpCountHook()
		{
			IL.RoR2.CharacterBody.RecalculateStats += (il) =>
			{
				ILCursor c = new ILCursor(il);

				int index = -1;

				bool found = c.TryGotoNext(
					x => x.MatchLdarg(0),
					x => x.MatchLdarg(0),
					x => x.MatchLdfld<CharacterBody>("baseJumpCount"),
					x => x.MatchLdloc(out index)
				);

				if (found)
				{
					// add
					c.Emit(OpCodes.Ldarg, 0);
					c.Emit(OpCodes.Ldloc, index);
					c.EmitDelegate<Func<CharacterBody, int, int>>((self, value) =>
					{
						if (self.HasBuff(BonusJumpContent.Buffs.JumpAvailable) || self.HasBuff(BonusJumpContent.Buffs.JumpWaiting)) value++;

						return value;
					});
					c.Emit(OpCodes.Stloc, index);
				}
				else
				{
					LogWarn("JumpCountHook Failed");
				}
			};
		}



		private static void IndicatorBuffDisplayHook()
		{
			On.RoR2.BuffCatalog.Init += (orig) =>
			{
				orig();

				HoistIndicatorBuffs();
			};
		}

		private static void HoistIndicatorBuffs()
		{
			BuffIndex jumpAvailable = availableBuff.buffIndex;
			BuffIndex jumpWaiting = waitingBuff.buffIndex;
			BuffIndex jumpCooldown = cooldownBuff.buffIndex;

			BuffIndex[] priorityDisplayBuffs = { jumpAvailable, jumpWaiting, jumpCooldown };

			List<BuffIndex> visibleBuffs = new List<BuffIndex>(BuffCatalog.nonHiddenBuffIndices);
			visibleBuffs.RemoveAll(t => t == jumpAvailable || t == jumpWaiting || t == jumpCooldown);

			List<BuffIndex> replacementBuffs = new List<BuffIndex>();
			replacementBuffs.AddRange(priorityDisplayBuffs);
			replacementBuffs.AddRange(visibleBuffs);

			BuffCatalog.nonHiddenBuffIndices = replacementBuffs.ToArray();
		}



		private static void HandleItemBehavior(CharacterBody body)
		{
			int applyBehavior = body.isPlayerControlled ? 1 : 0;

			body.AddItemBehavior<AuthBonusJumpBehavior>(applyBehavior);

			if (NetworkServer.active)
			{
				body.AddItemBehavior<ServerBonusJumpBehavior>(applyBehavior);
			}
		}



		private static void HandleCleanseHook()
		{
			On.RoR2.Util.CleanseBody += (orig, body, debuff, buff, cooldown, dot, stun, proj) =>
			{
				if (body && cooldown)
				{
					ServerBonusJumpBehavior behavior = body.GetComponent<ServerBonusJumpBehavior>();
					if (behavior)
					{
						behavior.CleanseCooldown();
					}
				}

				orig(body, debuff, buff, cooldown, dot, stun, proj);
			};
		}

		private static void UsageRequestedHook()
		{
			On.RoR2.CharacterBody.AddTimedBuff_BuffIndex_float += (orig, body, buff, duration) =>
			{
				if (buff == waitingBuff.buffIndex)
				{
					ServerBonusJumpBehavior behavior = body.GetComponent<ServerBonusJumpBehavior>();
					if (behavior)
					{
						behavior.ApplyUsageRequest();
					}

					return;
				}

				orig(body, buff, duration);
			};
		}



		private static void AllocateBehaviorBuffs()
		{
			ServerBonusJumpBehavior.availableBuff = availableBuff.buffIndex;
			ServerBonusJumpBehavior.waitingBuff = waitingBuff.buffIndex;
			ServerBonusJumpBehavior.cooldownBuff = cooldownBuff.buffIndex;

			AuthBonusJumpBehavior.availableBuff = availableBuff.buffIndex;
			AuthBonusJumpBehavior.waitingBuff = waitingBuff.buffIndex;
		}



		private static void PaladinCompat()
		{
			if (PluginLoaded("com.rob.Paladin"))
			{
				BaseUnityPlugin Plugin = BepInEx.Bootstrap.Chainloader.PluginInfos["com.rob.Paladin"].Instance;
				Assembly PluginAssembly = Assembly.GetAssembly(Plugin.GetType());

				Type type = Type.GetType("PaladinMod.States.AirSlam, " + PluginAssembly.FullName, false);
				if (type != null)
				{
					MethodInfo methodInfo = type.GetMethod("OnEnter", Flags);
					if (methodInfo != null)
					{
						HookEndpointManager.Modify(methodInfo, (ILContext.Manipulator)PaladinAirSlamHook);
					}
					else
					{
						LogWarn("[PaladinAirSlam] - Could Not Find Method : AirSlam.OnEnter");
					}
				}
				else
				{
					LogWarn("[PaladinAirSlam] - Could Not Find Type : PaladinMod.States.AirSlam");
				}
			}
		}

		private static void PaladinAirSlamHook(ILContext il)
		{
			ILCursor c = new ILCursor(il);

			bool found = c.TryGotoNext(
				x => x.MatchCallOrCallvirt<CharacterBody>("get_maxJumpCount"),
				x => x.MatchStfld<CharacterMotor>("jumpCount")
			);

			if (found)
			{
				c.Emit(OpCodes.Ldarg, 0);
				c.EmitDelegate<Action<EntityState>>((entityState) =>
				{
					CharacterMotor motor = entityState.characterMotor;
					CharacterBody body = entityState.characterBody;

					if (motor.jumpCount < body.maxJumpCount)
					{
						AuthBonusJumpBehavior behavior = body.GetComponent<AuthBonusJumpBehavior>();
						if (behavior)
						{
							behavior.DontConsume();
						}
					}
				});
			}
			else
			{
				LogWarn("PaladinAirSlamHook Failed");
			}
		}
	}
}
