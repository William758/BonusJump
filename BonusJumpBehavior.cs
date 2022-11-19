using UnityEngine;
using UnityEngine.Networking;
using RoR2;

using static TPDespair.BonusJump.BonusJumpPlugin;

namespace TPDespair.BonusJump
{
	public class ServerBonusJumpBehavior : CharacterBody.ItemBehavior
	{
		internal static BuffIndex availableBuff = BuffIndex.None;
		internal static BuffIndex waitingBuff = BuffIndex.None;
		internal static BuffIndex cooldownBuff = BuffIndex.None;

		private enum JumpState
		{
			Cooldown,
			Waiting,
			Available
		}

		private CharacterMotor motor;
		private JumpState jumpState = JumpState.Cooldown;

		private float timer = -1f;
		private bool cleanseCooldown = false;
		private bool requestedUsage = false;

		private float timeSinceLastGrounded = 0.25f;
		private float groundThreshold = 0.25f;

		public void Awake()
		{
			enabled = false;
		}

		public void OnEnable()
		{
			if (body)
			{
				CharacterMotor bodyMotor = body.characterMotor;
				if (bodyMotor) motor = bodyMotor;

				jumpState = JumpState.Cooldown;

				float cooldown = JumpCooldown.Value;
				if (cooldown >= 1f) timer = cooldown;

				groundThreshold = GroundThreshold.Value;
				timeSinceLastGrounded = groundThreshold;

				ClearDisplayBuffs();
			}
		}

		public void OnDisable()
		{
			if (body)
			{
				ClearDisplayBuffs();
			}
		}

		public void FixedUpdate()
		{
			if (NetworkServer.active && timer >= 0f)
			{
				if (body && motor)
				{
					UpdateGroundTracking();

					UpdateJumpState();

					UpdateBuffDisplay();
				}
			}
		}



		private void UpdateGroundTracking()
		{
			float deltaTime = Time.fixedDeltaTime;

			if (motor.isGrounded) timeSinceLastGrounded = 0f;
			else timeSinceLastGrounded += deltaTime;

			if (timeSinceLastGrounded <= groundThreshold)
			{
				timer = Mathf.Max(0f, timer - deltaTime);
			}
		}

		private void UpdateJumpState()
		{
			if (jumpState == JumpState.Cooldown && timer <= 0f)
			{
				jumpState = JumpState.Available;
			}

			if (jumpState == JumpState.Available && requestedUsage)
			{
				jumpState = JumpState.Waiting;

				requestedUsage = false;
			}

			if (jumpState == JumpState.Waiting && motor.isGrounded)
			{
				if (cleanseCooldown)
				{
					jumpState = JumpState.Available;

					timer = 0f;
				}
				else
				{
					jumpState = JumpState.Cooldown;

					float cooldownTime = JumpCooldown.Value;

					float cloudValue = CloudCompat.Value;
					if (cloudValue > 0f)
					{
						if (RainbowCloudItem != ItemIndex.None)
						{
							Inventory inventory = body.inventory;
							if (inventory)
							{
								int count = inventory.GetItemCount(RainbowCloudItem);
								if (count > 0)
								{
									cooldownTime *= 1f - (Util.ConvertAmplificationPercentageIntoReductionPercentage(cloudValue * 100f * count) / 100f);
								}
							}
						}
					}

					timer = Mathf.Max(1f, cooldownTime);
				}
			}

			if (jumpState == JumpState.Cooldown && cleanseCooldown)
			{
				jumpState = JumpState.Available;

				timer = 0f;
			}

			if (jumpState != JumpState.Waiting)
			{
				cleanseCooldown = false;
			}
		}



		private void ClearDisplayBuffs()
		{
			if (NetworkServer.active)
			{
				body.SetBuffCount(availableBuff, 0);
				body.SetBuffCount(waitingBuff, 0);
				body.SetBuffCount(cooldownBuff, 0);
			}
		}

		private void UpdateBuffDisplay()
		{
			if (jumpState == JumpState.Available)
			{
				if (!body.HasBuff(availableBuff))
				{
					body.SetBuffCount(availableBuff, 1);
				}

				if (body.HasBuff(waitingBuff))
				{
					body.SetBuffCount(waitingBuff, 0);
				}

				if (body.HasBuff(cooldownBuff))
				{
					body.SetBuffCount(cooldownBuff, 0);
				}
			}
			else
			{
				if (body.HasBuff(availableBuff))
				{
					body.SetBuffCount(availableBuff, 0);
				}

				if (jumpState == JumpState.Waiting)
				{
					if (!body.HasBuff(waitingBuff))
					{
						body.SetBuffCount(waitingBuff, 1);
					}

					if (body.HasBuff(cooldownBuff))
					{
						body.SetBuffCount(cooldownBuff, 0);
					}
				}
				else
				{
					if (body.HasBuff(waitingBuff))
					{
						body.SetBuffCount(waitingBuff, 0);
					}

					int displayValue = Mathf.Max(1, Mathf.CeilToInt(timer));
					int buffCount = body.GetBuffCount(cooldownBuff);
					if (buffCount != displayValue)
					{
						body.SetBuffCount(cooldownBuff, displayValue);
					}
				}
			}
		}



		public void CleanseCooldown()
		{
			if (NetworkServer.active)
			{
				cleanseCooldown = true;
			}
		}

		public void ApplyUsageRequest()
		{
			if (NetworkServer.active)
			{
				requestedUsage = true;
			}
		}
	}



	public class AuthBonusJumpBehavior : CharacterBody.ItemBehavior
	{
		internal static BuffIndex availableBuff = BuffIndex.None;
		internal static BuffIndex waitingBuff = BuffIndex.None;

		private CharacterMotor motor;

		private bool broadcastedUsage = false;
		private bool dontConsumeBonus = false;

		private int prevJumpCount = 0;
		private float timeSinceLastChanged = 0f;

		public void Awake()
		{
			enabled = false;
		}

		public void OnEnable()
		{
			if (body)
			{
				CharacterMotor bodyMotor = body.characterMotor;
				if (bodyMotor) motor = bodyMotor;
			}
		}

		public void FixedUpdate()
		{
			if (IsAuthority())
			{
				if (body && motor)
				{
					UpdateJumpTracking();
					UpdateJumpState();
				}
			}
		}



		private void UpdateJumpTracking()
		{
			if (motor.jumpCount != prevJumpCount)
			{
				dontConsumeBonus = false;

				if (ReachedMaxJumpCount() && (timeSinceLastChanged <= 0.125f || (motor.jumpCount - prevJumpCount) > 1))
				{
					dontConsumeBonus = true;
				}

				timeSinceLastChanged = 0f;

				prevJumpCount = motor.jumpCount;
			}
			else
			{
				timeSinceLastChanged += Time.fixedDeltaTime;
			}
		}

		private void UpdateJumpState()
		{
			if (body.HasBuff(availableBuff))
			{
				if (ReachedMaxJumpCount() && !dontConsumeBonus && !broadcastedUsage)
				{
					broadcastedUsage = true;
					body.AddTimedBuffAuthority(waitingBuff, 0f);
				}
			}

			if (motor.jumpCount == 0 && broadcastedUsage)
			{
				broadcastedUsage = false;
			}
		}



		private bool ReachedMaxJumpCount()
		{
			return motor.jumpCount >= body.maxJumpCount && body.maxJumpCount > 0;
		}

		private bool IsAuthority()
		{
			return NetworkClient.active && body.hasEffectiveAuthority;
		}



		public void DontConsume()
		{
			if (IsAuthority() && body.maxJumpCount > 0)
			{
				dontConsumeBonus = true;
				prevJumpCount = body.maxJumpCount;
				timeSinceLastChanged = 0f;
			}
		}
	}
}
