#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows.Controls;
using System.Windows.Media;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public enum PerfilReplicacion
	{
		Personalizado,
		Maestra_150K_a_Esclava_50K,
		Maestra_50K_a_Esclava_50K,
		Micros_1a1
	}

	/// <summary>
	/// Replicador Multicuenta Futuros Automatizado para NinjaTrader 8 con Interfaz WPF HUD.
	/// Permite replicar ejecuciones desde una cuenta Maestra hacia múltiples cuentas Esclavas.
	/// </summary>
	public class ReplicadorMulticuentaFuturos : Strategy
	{
		private List<Account> slaveAccountsList = new List<Account>();
		private bool isReplicationActive = true;

		// Elementos WPF de la Interfaz HUD
		private Grid chartGrid;
		private Border hudBorder;
		private TextBlock statusText;
		private TextBlock infoText;
		private Button btnFlattenAll;
		private Button btnToggleRepl;
		private Button btnSyncAccounts;

		// Estado y Pinceles para Análisis en Gráfico (Chart Painting)
		private int replTradeIdx = 0;
		private bool lastReplicationState = true;
		private static readonly SolidColorBrush BrushActiveZone = new SolidColorBrush(Color.FromArgb(22, 16, 185, 129));
		private static readonly SolidColorBrush BrushPauseZone  = new SolidColorBrush(Color.FromArgb(28, 245, 158, 11));
		private static readonly SolidColorBrush BrushErrorZone  = new SolidColorBrush(Color.FromArgb(28, 239, 68, 68));

		#region Properties - Configuración del Copiador
		[NinjaScriptProperty]
		[Display(Name = "Perfil Predeterminado de Réplica", Description = "Selecciona la proporción predeterminada entre cuentas (ej: Maestra 150K a Esclava 50K ajusta el multiplicador a 0.2x).", Order = 1, GroupName = "1. Configuración Copiador")]
		public PerfilReplicacion PerfilCopia { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Habilitar Copiador", Description = "Activar la replicación automática de órdenes", Order = 2, GroupName = "1. Configuración Copiador")]
		public bool CopierEnabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cuenta Maestra (Master)", Description = "Nombre exacto de la cuenta origen (ej: Sim101, PA_APEX_1)", Order = 2, GroupName = "1. Configuración Copiador")]
		public string MasterAccountName { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cuentas Esclavas (Slaves)", Description = "Cuentas destino separadas por coma (ej: PA_APEX_2, PA_APEX_3)", Order = 3, GroupName = "1. Configuración Copiador")]
		public string SlaveAccountNames { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Factor de Multiplicación", Description = "Multiplicador de tamaño de contratos para cuentas destino", Order = 4, GroupName = "1. Configuración Copiador")]
		public double Multiplier { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Copiar Entradas", Description = "Replicar órdenes de entrada", Order = 5, GroupName = "1. Configuración Copiador")]
		public bool CopyEntries { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Copiar Salidas / Exits", Description = "Replicar órdenes de salida o cierres", Order = 6, GroupName = "1. Configuración Copiador")]
		public bool CopyExits { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Bloquear Inversión de Posición", Description = "Evitar que las cuentas esclavas abran en dirección contraria", Order = 7, GroupName = "1. Configuración Copiador")]
		public bool BlockPositionInversion { get; set; }
		#endregion

		private List<Account> slaveAccountsList = new List<Account>();

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description							= "Herramienta de replicación y copia de operaciones multicuenta de futuros para NinjaTrader 8.";
				Name								= "ReplicadorMulticuentaFuturos";
				Calculate							= Calculate.OnPriceChange;
				IsInstantiatedOnEachOptimizationProperty = false;

				CopierEnabled						= true;
				MasterAccountName					= "Sim101";
				SlaveAccountNames					= "PA_APEX_002, PA_APEX_003";
				Multiplier							= 1.0;
				CopyEntries							= true;
				CopyExits							= true;
				BlockPositionInversion				= true;
			}
			else if (State == State.Configure)
			{
				ApplyReplicationPreset();
			}
			else if (State == State.DataLoaded)
			{
				InitializeSlaveAccounts();
			}
			else if (State == State.Historical)
			{
				if (ChartControl != null)
				{
					ChartControl.Dispatcher.InvokeAsync(() => { CreateWpfHudPanel(); });
				}
			}
			else if (State == State.Terminated)
			{
				if (ChartControl != null)
				{
					ChartControl.Dispatcher.InvokeAsync(() => { DisposeWpfHudPanel(); });
				}
			}
		}

		private void InitializeSlaveAccounts()
		{
			slaveAccountsList.Clear();
			lock (Account.All)
			{
				bool autoDetect = string.IsNullOrWhiteSpace(SlaveAccountNames) || 
				                 SlaveAccountNames.Trim().Equals("AUTO", StringComparison.OrdinalIgnoreCase) || 
				                 SlaveAccountNames.Trim().Equals("TODAS", StringComparison.OrdinalIgnoreCase);

				string[] names = autoDetect ? new string[0] : SlaveAccountNames.Split(',');

				foreach (Account acc in Account.All)
				{
					// Evitar agregar la cuenta maestra como esclava de sí misma
					if (acc.Name.Equals(MasterAccountName.Trim(), StringComparison.OrdinalIgnoreCase))
						continue;

					if (autoDetect)
					{
						slaveAccountsList.Add(acc);
						Print("[COPIADOR AUTO-DETECCIÓN] Cuenta conectada vinculada automáticamente: " + acc.Name);
					}
					else
					{
						foreach (string name in names)
						{
							if (acc.Name.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
							{
								slaveAccountsList.Add(acc);
								Print("[COPIADOR] Cuenta esclava vinculada exitosamente: " + acc.Name);
							}
						}
					}
				}
			}
		}

		private void ApplyReplicationPreset()
		{
			if (PerfilCopia == PerfilReplicacion.Maestra_150K_a_Esclava_50K)
			{
				Multiplier = 0.2;
				Print("[PRESET REPLICADOR] Perfil Maestra 150K -> Esclava 50K aplicado. Multiplicador ajustado automáticamente a 0.2x.");
			}
			else if (PerfilCopia == PerfilReplicacion.Maestra_50K_a_Esclava_50K || PerfilCopia == PerfilReplicacion.Micros_1a1)
			{
				Multiplier = 1.0;
				Print("[PRESET REPLICADOR] Perfil 1:1 aplicado. Multiplicador ajustado a 1.0x.");
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 1) return;
			PaintChartReplicatorSpanish();
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (!CopierEnabled || execution == null || execution.Account == null)
				return;

			// Verificar si la ejecución proviene de la Cuenta Maestra
			if (!execution.Account.Name.Equals(MasterAccountName.Trim(), StringComparison.OrdinalIgnoreCase))
				return;

			int slaveQuantity = (int)Math.Max(1, Math.Round(quantity * Multiplier));

			// Replicar en cada cuenta esclava
			foreach (Account slaveAcc in slaveAccountsList)
			{
				try
				{
					OrderAction action = (execution.Order.OrderAction == OrderAction.Buy || execution.Order.OrderAction == OrderAction.BuyToCover) 
						? OrderAction.Buy 
						: OrderAction.Sell;

					if ((action == OrderAction.Buy || action == OrderAction.Sell) && CopyEntries)
					{
						Order slaveOrder = slaveAcc.CreateOrder(Instrument, action, OrderType.Market, OrderEntry.Manual, TimeInForce.Gtc, slaveQuantity, 0, 0, "", "Copied_Order", DateTime.MaxValue, null);
						slaveAcc.Submit(new[] { slaveOrder });
						Print("[COPIADOR] Orden replicada a cuenta: " + slaveAcc.Name + " | Cantidad: " + slaveQuantity);
					}
				}
				catch (Exception ex)
				{
					Print("[ERROR COPIADOR] Error al replicar en " + slaveAcc.Name + ": " + ex.Message);
				}
			}

			// Pintar marcadores de replicación en gráfico
			bool isLong = (execution.Order.OrderAction == OrderAction.Buy || execution.Order.OrderAction == OrderAction.BuyToCover);
			replTradeIdx++;

			if (isLong)
			{
				Draw.ArrowUp(this, "REPL_BUY_" + replTradeIdx, false, 0, Low[0] - TickSize * 6, new SolidColorBrush(Color.FromRgb(78, 222, 163)));
				Draw.Text(this, "REPL_BUY_LBL_" + replTradeIdx, false, $"▲ REPLICADO COMPRA {slaveQuantity}x → {slaveAccountsList.Count} esclava(s)", 0, Low[0] - TickSize * 16, 0, new SolidColorBrush(Color.FromRgb(78, 222, 163)), new SimpleFont("Arial", 8), TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
				Draw.HorizontalLine(this, "REPL_LEVEL_" + replTradeIdx, price, new Stroke(new SolidColorBrush(Color.FromArgb(120, 78, 222, 163)), DashStyleHelper.DashDot, 1));
			}
			else
			{
				Draw.ArrowDown(this, "REPL_SELL_" + replTradeIdx, false, 0, High[0] + TickSize * 6, new SolidColorBrush(Color.FromRgb(255, 100, 100)));
				Draw.Text(this, "REPL_SELL_LBL_" + replTradeIdx, false, $"▼ REPLICADO VENTA {slaveQuantity}x → {slaveAccountsList.Count} esclava(s)", 0, High[0] + TickSize * 16, 0, Brushes.Tomato, new SimpleFont("Arial", 8), TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
				Draw.HorizontalLine(this, "REPL_LEVEL_" + replTradeIdx, price, new Stroke(new SolidColorBrush(Color.FromArgb(120, 255, 100, 100)), DashStyleHelper.DashDot, 1));
			}
		}

		private void PaintChartReplicatorSpanish()
		{
			if (!CopierEnabled || !isReplicationActive)
			{
				BackBrushes[0] = BrushPauseZone;
				Draw.Text(this, "REPL_STATUS_TXT_SP", false, "⏸️ REPLICACIÓN PAUSADA", 0, High[0] + TickSize * 5, 0, Brushes.Orange, new SimpleFont("Arial", 9), TextAlignment.Right, Brushes.Orange, new SolidColorBrush(Color.FromArgb(40, 255, 150, 0)), 80);
				if (lastReplicationState)
				{
					lastReplicationState = false;
					Draw.VerticalLine(this, "REPL_PAUSE_EVT_" + CurrentBar, 0, new Stroke(Brushes.Orange, DashStyleHelper.Dash, 2));
				}
				return;
			}

			BackBrushes[0] = BrushActiveZone;
			if (!lastReplicationState)
			{
				lastReplicationState = true;
				Draw.VerticalLine(this, "REPL_RESUME_EVT_" + CurrentBar, 0, new Stroke(new SolidColorBrush(Color.FromRgb(16, 185, 129)), DashStyleHelper.Dash, 2));
			}

			string statusTxt = $"⚡ REPLICADOR ACTIVO\n" +
			                   $"Maestra: {MasterAccountName}\n" +
			                   $"Esclavas: {slaveAccountsList.Count}  |  Ratio: {Multiplier:F2}x\n" +
			                   $"Trades Replicados: {replTradeIdx}";

			Draw.Text(this, "REPL_STATUS_TXT_SP", false, statusTxt, 0, High[0] + TickSize * 6, 0, new SolidColorBrush(Color.FromRgb(78, 222, 163)), new SimpleFont("Arial", 8), TextAlignment.Right, new SolidColorBrush(Color.FromRgb(16, 185, 129)), new SolidColorBrush(Color.FromArgb(50, 0, 80, 40)), 80);
		}
	}
}
