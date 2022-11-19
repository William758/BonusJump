using RoR2;
using RoR2.ContentManagement;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TPDespair.BonusJump
{
	public class BonusJumpContent : IContentPackProvider
	{
		public ContentPack contentPack = new ContentPack();

		public string identifier
		{
			get { return "BonusJumpContent"; }
		}

		public IEnumerator LoadStaticContentAsync(LoadStaticContentAsyncArgs args)
		{
			BonusJumpPlugin.LoadAssets();

			Sprites.Create();

			Buffs.Create();

			SetInternalRef();

			contentPack.buffDefs.Add(Buffs.buffDefs.ToArray());

			args.ReportProgress(1f);
			yield break;
		}

		public IEnumerator GenerateContentPackAsync(GetContentPackAsyncArgs args)
		{
			ContentPack.Copy(contentPack, args.output);
			args.ReportProgress(1f);
			yield break;
		}

		public IEnumerator FinalizeAsync(FinalizeAsyncArgs args)
		{
			args.ReportProgress(1f);
			yield break;
		}



		public static class Buffs
		{
			public static BuffDef JumpAvailable;
			public static BuffDef JumpWaiting;
			public static BuffDef JumpCooldown;

			public static List<BuffDef> buffDefs = new List<BuffDef>();


			public static void Create()
			{
				JumpAvailable = ScriptableObject.CreateInstance<BuffDef>();
				JumpAvailable.name = "BonusJumpAvailable";
				JumpAvailable.buffColor = new Color(0.35f, 0.65f, 0.35f);
				JumpAvailable.canStack = false;
				JumpAvailable.isDebuff = false;
				JumpAvailable.iconSprite = Sprites.BonusJumpNormal;

				buffDefs.Add(JumpAvailable);

				JumpWaiting = ScriptableObject.CreateInstance<BuffDef>();
				JumpWaiting.name = "BonusJumpWaiting";
				JumpWaiting.buffColor = new Color(0.65f, 0.65f, 0.35f);
				JumpWaiting.canStack = false;
				JumpWaiting.isDebuff = false;
				JumpWaiting.iconSprite = Sprites.BonusJumpShaded;

				buffDefs.Add(JumpWaiting);

				JumpCooldown = ScriptableObject.CreateInstance<BuffDef>();
				JumpCooldown.name = "BonusJumpCooldown";
				JumpCooldown.buffColor = new Color(0.65f, 0.35f, 0.35f);
				JumpCooldown.canStack = true;
				JumpCooldown.isDebuff = false;
				JumpCooldown.iconSprite = Sprites.BonusJumpShaded;

				buffDefs.Add(JumpCooldown);
			}
		}

		public static class Sprites
		{
			public static Sprite BonusJumpNormal;
			public static Sprite BonusJumpShaded;

			public static void Create()
			{
				BonusJumpNormal = BonusJumpPlugin.Assets.LoadAsset<Sprite>("Assets/Icons/texBuffJump.png");
				BonusJumpShaded = BonusJumpPlugin.Assets.LoadAsset<Sprite>("Assets/Icons/texBuffJumpShaded.png");
			}
		}



		public static void SetInternalRef()
		{
			BonusJumpPlugin.availableBuff = Buffs.JumpAvailable;
			BonusJumpPlugin.waitingBuff = Buffs.JumpWaiting;
			BonusJumpPlugin.cooldownBuff = Buffs.JumpCooldown;
		}
	}
}