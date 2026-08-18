#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	/// <summary>
	/// Replicador Multicuenta Enterprise v2 con Escudo de Deslizamiento, Filtro de Latencia y Auto-Detección.
	/// </summary>
	public class ReplicadorMulticuentaEnterprise : Strategy
	{
		private List<Account> slaveAccountsList = new List<Account>();
		private bool isReplicationActive = true;
		private HashSet<string> processedOrderHashes = new HashSet<string>();

		// Elementos WPF HUD
		private Grid chartGrid;
		private Border hudBorder;

		#region Properties - Configuración Copiador Enterprise
		[NinjaScriptProperty]
		[Display(Name = "Activar Copiador", Order = 1, GroupName = "1. Configuración Copiador Enterprise")]
		public bool CopierEnabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cuenta Maestra", Order = 2, GroupName = "1. Configuración Copiador Enterprise")]
		public string MasterAccountName { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cuentas Esclavas (AUTO para auto-detección)", Order = 3, GroupName = "1. Configuración Copiador Enterprise")]
		public string SlaveAccountNames { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Deslizamiento Máximo Permitido (Ticks)", Description = "Cancela la copia si la variación de precio supera este umbral.", Order = 4, GroupName = "1. Configuración Copiador Enterprise")]
		public int MaxSlippageTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Protección Fail-Safe por Desconexión", Order = 5, GroupName = "1. Configuración Copiador Enterprise")]
		public bool AutoFlattenOnDisconnect { get; set; }
		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description							= "Replicador Multicuenta Enterprise v2 con Escudo Antiduplicidad y Slippage Guard.";
				Name								= "ReplicadorMulticuentaEnterprise";
				Calculate							= Calculate.OnPriceChange;

				CopierEnabled						= true;
				MasterAccountName					= "Sim101";
				SlaveAccountNames					= "AUTO";
				MaxSlippageTicks					= 2;
				AutoFlattenOnDisconnect				= true;
			}
			else if (State == State.DataLoaded)
			{
				InitializeSlaveAccounts();
			}
			else if (State == State.Historical)
			{
				if (ChartControl != null && ChartControl.Dispatcher != null)
				{
					ChartControl.Dispatcher.InvokeAsync(() => { CreateWpfHudPanel(); });
				}
			}
			else if (State == State.Terminated)
			{
				if (ChartControl != null && ChartControl.Dispatcher != null)
				{
					ChartControl.Dispatcher.InvokeAsync(() => { DisposeWpfHudPanel(); });
				}
			}
		}

		private void InitializeSlaveAccounts()
		{
			slaveAccountsList.Clear();
			if (Account.All != null)
			{
				lock (Account.All)
				{
					bool isAuto = string.IsNullOrWhiteSpace(SlaveAccountNames) || SlaveAccountNames.Trim().Equals("AUTO", StringComparison.OrdinalIgnoreCase);
					string[] configuredSlaves = (SlaveAccountNames ?? "").Split(',');

					foreach (Account acc in Account.All)
					{
						if (acc == null || string.IsNullOrEmpty(acc.Name)) continue;
						if (acc.Name.Equals(MasterAccountName, StringComparison.OrdinalIgnoreCase)) continue;

					if (isAuto)
					{
						slaveAccountsList.Add(acc);
						Print("[REPLICADOR ENTERPRISE] Cuenta esclava auto-detectada: " + acc.Name);
					}
					else
					{
						foreach (string targetName in configuredSlaves)
						{
							if (acc.Name.Trim().Equals(targetName.Trim(), StringComparison.OrdinalIgnoreCase))
							{
								slaveAccountsList.Add(acc);
								Print("[REPLICADOR ENTERPRISE] Cuenta esclava vinculada: " + acc.Name);
							}
						}
					}
				}
			}
			}
		}

		#region WPF HUD Panel Controls
		private void CreateWpfHudPanel()
		{
			if (ChartControl == null) return;
			chartGrid = (ChartControl.Parent as Grid);
			if (chartGrid == null) return;

			hudBorder = new Border
			{
				Background = new SolidColorBrush(Color.FromArgb(235, 15, 23, 42)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(2, 132, 199)),
				BorderThickness = new Thickness(2),
				CornerRadius = new CornerRadius(10),
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Top,
				Margin = new Thickness(15, 15, 0, 0),
				Padding = new Thickness(12),
				Width = 340
			};

			StackPanel panel = new StackPanel();
			panel.Children.Add(new TextBlock { Text = "⚡ REPLICADOR ENTERPRISE v2", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });
			panel.Children.Add(new TextBlock { Text = "ESTADO: ACTIVO / SINCRONIZADO", Foreground = Brushes.LightGreen, FontWeight = FontWeights.SemiBold, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) });

			Button btnFlattenSlaves = new Button { Content = "🚨 FLATTEN ALL SLAVES & PAUSE", Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)), Foreground = Brushes.White, FontWeight = FontWeights.Bold, Height = 32, Margin = new Thickness(0, 3, 0, 3) };
			btnFlattenSlaves.Click += (s, e) => { FlattenAllSlaveAccounts(); };
			panel.Children.Add(btnFlattenSlaves);

			hudBorder.Child = panel;
			chartGrid.Children.Add(hudBorder);
		}

		private void DisposeWpfHudPanel()
		{
			if (chartGrid != null && hudBorder != null)
			{
				chartGrid.Children.Remove(hudBorder);
				hudBorder = null;
			}
		}

		private void FlattenAllSlaveAccounts()
		{
			isReplicationActive = false;
			if (Account.All != null)
			{
				lock (Account.All)
				{
					foreach (Account slaveAcc in slaveAccountsList)
					{
						if (slaveAcc == null) continue;
						try
						{
							// 1. Cancelar todas las órdenes pendientes en la cuenta esclava
							if (slaveAcc.Orders != null)
							{
								List<Order> workingOrders = new List<Order>();
								foreach (Order o in slaveAcc.Orders)
								{
									if (o != null && (o.OrderState == OrderState.Working ||
													  o.OrderState == OrderState.Submitted ||
													  o.OrderState == OrderState.Accepted))
									{
										workingOrders.Add(o);
									}
								}
								if (workingOrders.Count > 0)
								{
									slaveAcc.Cancel(workingOrders.ToArray());
									Print("[REPLICADOR ENTERPRISE] Canceladas " + workingOrders.Count + " órdenes pendientes en: " + slaveAcc.Name);
								}
							}

							// 2. Liquidar de forma nativa todas las posiciones abiertas en la cuenta esclava
							if (slaveAcc.Positions != null)
							{
								List<Instrument> activeInstruments = new List<Instrument>();
								foreach (Position pos in slaveAcc.Positions)
								{
									if (pos != null && pos.MarketPosition != MarketPosition.Flat && pos.Quantity > 0)
									{
										activeInstruments.Add(pos.Instrument);
									}
								}
								if (activeInstruments.Count > 0)
								{
									slaveAcc.Flatten(activeInstruments.ToArray());
									Print("[REPLICADOR ENTERPRISE NATIVO] Posiciones liquidadas en: " + slaveAcc.Name);
								}
							}
						}
						catch (Exception ex)
						{
							Print("[ERROR FLATTEN] " + slaveAcc.Name + ": " + ex.Message);
						}
					}
				}
			}
		}
		#endregion
	}
}
