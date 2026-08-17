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
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows.Controls;
using System.Windows.Media;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public enum PerfilCuentaFondeo
	{
		AutoDeteccion,
		Cuenta_50K,
		Cuenta_100K,
		Cuenta_150K,
		Personalizado
	}

	/// <summary>
	/// Estrategia Automatizada de Futuros con Sistema Integrado de Validación de Cuentas e Interfaz WPF HUD.
	/// </summary>
	public class BotAperturaMercado : Strategy
	{
		#region Private Fields
		private EMA emaFast;
		private EMA emaMid;
		
		private double dailyCumProfit = 0;
		private int tradesTodayCount = 0;
		private DateTime currentTradeDate = DateTime.MinValue;
		private bool dailyPnLocked = false;
		private bool isAccountAuthorized = false;
		private bool isPaused = false;

		// Elementos WPF de la Interfaz HUD
		private Grid chartGrid;
		private Border hudBorder;
		private TextBlock statusText;
		private TextBlock pnlText;
		private Button btnFlatten;
		private Button btnPause;
		private Button btnReset;

		// Estado y Pinceles para Análisis en Gráfico (Chart Painting)
		private double pmHigh = double.MinValue;
		private double pmLow  = double.MaxValue;
		private int botTradeTagCounter = 0;
		private static readonly SolidColorBrush BrushPMZone = new SolidColorBrush(Color.FromArgb(35, 155, 89, 182));
		private static readonly SolidColorBrush BrushActiveZone = new SolidColorBrush(Color.FromArgb(18, 49, 152, 220));
		private static readonly SolidColorBrush BrushInTradeLong = new SolidColorBrush(Color.FromArgb(40, 16, 185, 129));
		private static readonly SolidColorBrush BrushInTradeShort = new SolidColorBrush(Color.FromArgb(40, 239, 68, 68));
		#endregion

		#region Properties - 0. Sistema de Licencia y Validación de Cuentas
		[NinjaScriptProperty]
		[Display(Name = "Activar Validación de Cuenta", Description = "Si está activo, solo las cuentas permitidas podrán ejecutar el bot.", Order = 1, GroupName = "0. Licencia & Validación")]
		public bool UseAccountValidation { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cuentas Autorizadas (separadas por coma)", Description = "Ejemplo: Sim101, PA_APEX_1234, MY_FUNDED_ACCOUNT", Order = 2, GroupName = "0. Licencia & Validación")]
		public string AuthorizedAccountNames { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Fecha Expiración Licencia (YYYYMMDD)", Description = "Formato AAAAMMDD. Ejemplo: 20261231. Usar 0 para sin expiración.", Order = 3, GroupName = "0. Licencia & Validación")]
		public int LicenseExpirationDate { get; set; }
		#endregion

		#region Properties - 1. General & Posición
		[NinjaScriptProperty]
		[Display(Name = "Perfil Predeterminado de Cuenta", Description = "Selecciona la configuración recomendada según el tamaño de tu cuenta de fondeo (50K, 100K, 150K o Personalizado).", Order = 1, GroupName = "1. General & Posición")]
		public PerfilCuentaFondeo PerfilCuenta { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Contratos / Lotes", Order = 2, GroupName = "1. General & Posición")]
		public int Contracts { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Habilitar Compras (Longs)", Order = 2, GroupName = "1. General & Posición")]
		public bool EnableLongs { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Habilitar Ventas (Shorts)", Order = 3, GroupName = "1. General & Posición")]
		public bool EnableShorts { get; set; }
		#endregion

		#region Properties - 2. EMAs & Indicadores
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Período EMA Rápida", Order = 1, GroupName = "2. Indicadores & Tendencia")]
		public int FastPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Período EMA Media", Order = 2, GroupName = "2. Indicadores & Tendencia")]
		public int MidPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Mostrar EMAs en Gráfico", Order = 3, GroupName = "2. Indicadores & Tendencia")]
		public bool ShowEMAs { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Usar Filtro Pendiente EMA", Order = 4, GroupName = "2. Indicadores & Tendencia")]
		public bool UseEmaSlopeFilter { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Pendiente Mínima Mid Long (Ticks)", Order = 5, GroupName = "2. Indicadores & Tendencia")]
		public double LongMinMidSlopeTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Usar Filtro de Apilamiento EMA", Order = 6, GroupName = "2. Indicadores & Tendencia")]
		public bool UseEmaStackFilter { get; set; }
		#endregion

		#region Properties - 3. Filtros de Señal
		[NinjaScriptProperty]
		[Display(Name = "Usar Filtro de Cuerpo de Vela", Order = 1, GroupName = "3. Filtros de Señal")]
		public bool UseBaseBodyFilter { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cuerpo Mínimo de Vela (Ticks)", Order = 2, GroupName = "3. Filtros de Señal")]
		public int BaseMinSignalBodyTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cuerpo Máximo de Vela (Ticks)", Order = 3, GroupName = "3. Filtros de Señal")]
		public int BaseMaxSignalBodyTicks { get; set; }
		#endregion

		#region Properties - 4. Filtro Horario
		[NinjaScriptProperty]
		[Display(Name = "Hora Inicio Sesión (HHMMSS)", Order = 1, GroupName = "4. Filtro Horario")]
		public int StartTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Hora Fin Sesión (HHMMSS)", Order = 2, GroupName = "4. Filtro Horario")]
		public int EndTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cerrar Posición al Finalizar Horario", Order = 3, GroupName = "4. Filtro Horario")]
		public bool ExitAfterEndTime { get; set; }
		#endregion

		#region Properties - 5. Control de Riesgo Diario
		[NinjaScriptProperty]
		[Display(Name = "Usar Bloqueo PnL Diario", Order = 1, GroupName = "5. Control de Riesgo Diario")]
		public bool UseDailyPnLLock { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Pérdida Máxima Diaria ($)", Order = 2, GroupName = "5. Control de Riesgo Diario")]
		public double DailyMaxLossDollars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Objetivo de Ganancia Diario ($)", Order = 3, GroupName = "5. Control de Riesgo Diario")]
		public double DailyProfitTargetDollars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Máximo Operaciones por Día", Order = 4, GroupName = "5. Control de Riesgo Diario")]
		public int MaxTradesPerDay { get; set; }
		#endregion

		#region Properties - 6. Stops & Objetivos
		[NinjaScriptProperty]
		[Display(Name = "Long Stop Loss (Ticks)", Order = 1, GroupName = "6. Stops & Objetivos")]
		public int LongStopLossTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Short Stop Loss (Ticks)", Order = 2, GroupName = "6. Stops & Objetivos")]
		public int ShortStopLossTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Long Profit Target (Ticks)", Order = 3, GroupName = "6. Stops & Objetivos")]
		public int LongProfitTargetTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Short Profit Target (Ticks)", Order = 4, GroupName = "6. Stops & Objetivos")]
		public int ShortProfitTargetTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Usar Breakeven", Order = 5, GroupName = "6. Stops & Objetivos")]
		public bool UseBreakeven { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Trigger Breakeven Long (Ticks)", Order = 6, GroupName = "6. Stops & Objetivos")]
		public int LongBreakevenTriggerTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Breakeven Offset Long (Ticks)", Order = 7, GroupName = "6. Stops & Objetivos")]
		public int LongBreakevenPlusTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Usar Trailing Stop", Order = 8, GroupName = "6. Stops & Objetivos")]
		public bool UseTrailingStop { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Trigger Trailing Stop Long (Ticks)", Order = 9, GroupName = "6. Stops & Objetivos")]
		public int LongTrailTriggerTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Distancia Trailing Stop Long (Ticks)", Order = 10, GroupName = "6. Stops & Objetivos")]
		public int LongTrailDistanceTicks { get; set; }
		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description							= "Estrategia de trading de futuros personalizada para apertura de mercado con validación de licencias y cuentas autorizadas.";
				Name								= "BotAperturaMercado";
				Calculate							= Calculate.OnBarClose;
				IsInstantiatedOnEachOptimizationProperty = true;
				IsExitOnSessionCloseStrategy		= true;
				ExitOnSessionCloseSeconds			= 30;
				BarsRequiredToTrade					= 20;

				// Configuración de Licencia
				UseAccountValidation				= true;
				AuthorizedAccountNames				= "Sim101, PA_APEX_123456, MI_CUENTA_REAL";
				LicenseExpirationDate				= 20261231; // Ejemplo: 31 de Diciembre de 2026

				// Parámetros por Defecto
				Contracts							= 1;
				EnableLongs							= true;
				EnableShorts						= true;

				FastPeriod							= 9;
				MidPeriod							= 21;
				ShowEMAs							= true;
				UseEmaSlopeFilter					= true;
				LongMinMidSlopeTicks				= 0.5;
				UseEmaStackFilter					= true;

				UseBaseBodyFilter					= true;
				BaseMinSignalBodyTicks				= 4;
				BaseMaxSignalBodyTicks				= 40;

				StartTime							= 93000;
				EndTime								= 154500;
				ExitAfterEndTime					= true;

				UseDailyPnLLock						= true;
				DailyMaxLossDollars					= 500;
				DailyProfitTargetDollars			= 1000;
				MaxTradesPerDay						= 5;

				LongStopLossTicks					= 30;
				ShortStopLossTicks					= 30;
				LongProfitTargetTicks				= 60;
				ShortProfitTargetTicks				= 60;

				UseBreakeven						= true;
				LongBreakevenTriggerTicks			= 20;
				LongBreakevenPlusTicks				= 2;

				UseTrailingStop						= true;
				LongTrailTriggerTicks				= 30;
				LongTrailDistanceTicks				= 15;
			}
			else if (State == State.Configure)
			{
				ApplyAccountPreset();
				SetStopLoss(CalculationMode.Ticks, LongStopLossTicks);
				SetProfitTarget(CalculationMode.Ticks, LongProfitTargetTicks);
			}
			else if (State == State.DataLoaded)
			{
				// Validar cuenta y licencia al cargar datos
				ValidateAccountAndLicense();

				emaFast = EMA(FastPeriod);
				emaMid = EMA(MidPeriod);

				if (ShowEMAs)
				{
					AddChartIndicator(emaFast);
					AddChartIndicator(emaMid);
				}
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

		/// <summary>
		/// Aplica automáticamente los parámetros predeterminados según el perfil de cuenta seleccionado o auto-detectado.
		/// </summary>
		private void ApplyAccountPreset()
		{
			PerfilCuentaFondeo perfilAAplicar = PerfilCuenta;

			if (perfilAAplicar == PerfilCuentaFondeo.AutoDeteccion)
			{
				double cash = 50000;
				string name = "";
				if (Account != null)
				{
					try { cash = Account.Get(AccountItem.CashValue, Currency.Usd); } catch {}
					name = Account.Name.ToUpper();
				}

				if (cash >= 140000 || name.Contains("150K") || name.Contains("150000"))
				{
					perfilAAplicar = PerfilCuentaFondeo.Cuenta_150K;
					Print("[AUTO-DETECCIÓN INTELIGENTE] Cuenta de 150K detectada automáticamente (" + (Account != null ? Account.Name : "") + "). Aplicando perfil 150K (15 NQ, -$1500 Max Loss, Target +$3000).");
				}
				else if (cash >= 90000 || name.Contains("100K") || name.Contains("100000"))
				{
					perfilAAplicar = PerfilCuentaFondeo.Cuenta_100K;
					Print("[AUTO-DETECCIÓN INTELIGENTE] Cuenta de 100K detectada automáticamente (" + (Account != null ? Account.Name : "") + "). Aplicando perfil 100K (5 NQ, -$1000 Max Loss, Target +$2000).");
				}
				else
				{
					perfilAAplicar = PerfilCuentaFondeo.Cuenta_50K;
					Print("[AUTO-DETECCIÓN INTELIGENTE] Cuenta de 50K detectada automáticamente (" + (Account != null ? Account.Name : "") + "). Aplicando perfil 50K (2 NQ, -$500 Max Loss, Target +$1000).");
				}
			}

			if (perfilAAplicar == PerfilCuentaFondeo.Cuenta_50K)
			{
				Contracts = 2;
				DailyMaxLossDollars = 500;
				DailyProfitTargetDollars = 1000;
				LongStopLossTicks = 30;
				ShortStopLossTicks = 30;
				LongProfitTargetTicks = 60;
				ShortProfitTargetTicks = 60;
				Print("[PRESET APLICADO] Perfil Cuenta 50K Fondeo: 2 Contratos NQ, Max Loss $500, Target $1000.");
			}
			else if (perfilAAplicar == PerfilCuentaFondeo.Cuenta_100K)
			{
				Contracts = 5;
				DailyMaxLossDollars = 1000;
				DailyProfitTargetDollars = 2000;
				LongStopLossTicks = 40;
				ShortStopLossTicks = 40;
				LongProfitTargetTicks = 80;
				ShortProfitTargetTicks = 80;
				Print("[PRESET APLICADO] Perfil Cuenta 100K Fondeo: 5 Contratos NQ, Max Loss $1000, Target $2000.");
			}
			else if (perfilAAplicar == PerfilCuentaFondeo.Cuenta_150K)
			{
				Contracts = 15;
				DailyMaxLossDollars = 1500;
				DailyProfitTargetDollars = 3000;
				LongStopLossTicks = 103;
				ShortStopLossTicks = 103;
				LongProfitTargetTicks = 384;
				ShortProfitTargetTicks = 384;
				Print("[PRESET APLICADO] Perfil Cuenta 150K Fondeo: 15 Contratos NQ, Max Loss $1500, Target $3000.");
			}
		}

		/// <summary>
		/// Sistema de Validación de Licencia por Nombre de Cuenta y Expiración.
		/// </summary>
		private void ValidateAccountAndLicense()
		{
			if (!UseAccountValidation)
			{
				isAccountAuthorized = true;
				return;
			}

			// 1. Verificar Fecha de Expiración
			if (LicenseExpirationDate > 0)
			{
				int todayInt = int.Parse(DateTime.Now.ToString("yyyyMMdd"));
				if (todayInt > LicenseExpirationDate)
				{
					isAccountAuthorized = false;
					Print("[LICENCIA EXPIRADA] La licencia de esta estrategia venció en la fecha: " + LicenseExpirationDate);
					Draw.TextFixed(this, "LicenseError", "ERROR: Licencia Expirada (" + LicenseExpirationDate + ")", TextPosition.Center, Brushes.Red, new Gui.Tools.SimpleFont("Arial", 16), Brushes.Black, Brushes.Red, 100);
					return;
				}
			}

			// 2. Verificar Nombre de la Cuenta Activa
			string currentAccount = Account != null ? Account.Name.Trim() : "";
			string[] allowedAccounts = AuthorizedAccountNames.Split(',');
			bool matchFound = false;

			foreach (string acc in allowedAccounts)
			{
				if (acc.Trim().Equals(currentAccount, StringComparison.OrdinalIgnoreCase))
				{
					matchFound = true;
					break;
				}
			}

			if (!matchFound)
			{
				isAccountAuthorized = false;
				Print("[LICENCIA DENEGADA] La cuenta '" + currentAccount + "' NO está autorizada.");
				Draw.TextFixed(this, "LicenseError", "ACCESO DENEGADO: Cuenta '" + currentAccount + "' no autorizada.", TextPosition.Center, Brushes.Red, new Gui.Tools.SimpleFont("Arial", 16), Brushes.Black, Brushes.Red, 100);
			}
			else
			{
				isAccountAuthorized = true;
				Print("[LICENCIA CORRECTA] Cuenta '" + currentAccount + "' verificada exitosamente.");
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < BarsRequiredToTrade || !isAccountAuthorized)
				return;

			// Restablecimiento de métricas al cambiar de día
			if (Time[0].Date != currentTradeDate)
			{
				currentTradeDate = Time[0].Date;
				dailyCumProfit = 0;
				tradesTodayCount = 0;
				dailyPnLocked = false;
				pmHigh = double.MinValue;
				pmLow = double.MaxValue;
			}

			// Pintar análisis visual en velas (Chart Painting)
			PaintChartSpanish();

			if (UseDailyPnLLock && dailyPnLocked)
				return;

			// Filtro Horario con Aviso de Protección
			int timeNow = ToTime(Time[0]);
			if (timeNow < StartTime || timeNow > EndTime)
			{
				Draw.TextFixed(this, "TimeProtection", "ESTADO: BLOQUEADO POR PROTECCIÓN (FUERA DE HORARIO 09:30-15:50)", TextPosition.TopRight, Brushes.Orange, new Gui.Tools.SimpleFont("Arial", 12), Brushes.Black, Brushes.DarkOrange, 90);

				if (ExitAfterEndTime && Position.MarketPosition != MarketPosition.Flat)
				{
					if (Position.MarketPosition == MarketPosition.Long) ExitLong();
					else if (Position.MarketPosition == MarketPosition.Short) ExitShort();
				}
				return;
			}
			else
			{
				RemoveDrawObject("TimeProtection");
			}

			if (tradesTodayCount >= MaxTradesPerDay || isPaused)
				return;

			double candleBodyTicks = Math.Abs(Close[0] - Open[0]) / TickSize;
			double midSlope = (emaMid[0] - emaMid[1]) / TickSize;

			// ENTRADA LONG
			if (EnableLongs && Position.MarketPosition == MarketPosition.Flat)
			{
				bool emaTrendOk = !UseEmaStackFilter || (emaFast[0] > emaMid[0]);
				bool slopeOk = !UseEmaSlopeFilter || (midSlope >= LongMinMidSlopeTicks);
				bool bodyOk = !UseBaseBodyFilter || (candleBodyTicks >= BaseMinSignalBodyTicks && candleBodyTicks <= BaseMaxSignalBodyTicks);
				bool pullbackOk = (Low[0] <= emaFast[0] || Low[0] <= emaMid[0]);

				if (emaTrendOk && slopeOk && bodyOk && pullbackOk && Close[0] > Open[0])
				{
					SetStopLoss(CalculationMode.Ticks, LongStopLossTicks);
					SetProfitTarget(CalculationMode.Ticks, LongProfitTargetTicks);
					EnterLong(Contracts, "MiEstrategia_Long");
					tradesTodayCount++;
				}
			}
			// ENTRADA SHORT
			else if (EnableShorts && Position.MarketPosition == MarketPosition.Flat)
			{
				bool emaTrendOk = !UseEmaStackFilter || (emaFast[0] < emaMid[0]);
				bool slopeOk = !UseEmaSlopeFilter || (midSlope <= -LongMinMidSlopeTicks);
				bool bodyOk = !UseBaseBodyFilter || (candleBodyTicks >= BaseMinSignalBodyTicks && candleBodyTicks <= BaseMaxSignalBodyTicks);
				bool pullbackOk = (High[0] >= emaFast[0] || High[0] >= emaMid[0]);

				if (emaTrendOk && slopeOk && bodyOk && pullbackOk && Close[0] < Open[0])
				{
					SetStopLoss(CalculationMode.Ticks, ShortStopLossTicks);
					SetProfitTarget(CalculationMode.Ticks, ShortProfitTargetTicks);
					EnterShort(Contracts, "MiEstrategia_Short");
					tradesTodayCount++;
				}
			}

			// GESTIÓN DE POSICIÓN ABIERTA (Breakeven & Trailing Stop)
			if (Position.MarketPosition != MarketPosition.Flat)
			{
				double openProfitTicks = (Position.MarketPosition == MarketPosition.Long) 
					? (Close[0] - Position.AveragePrice) / TickSize
					: (Position.AveragePrice - Close[0]) / TickSize;

				if (UseBreakeven && openProfitTicks >= LongBreakevenTriggerTicks)
				{
					SetStopLoss(CalculationMode.Ticks, -LongBreakevenPlusTicks);
				}

				if (UseTrailingStop && openProfitTicks >= LongTrailTriggerTicks)
				{
					double trailPrice = (Position.MarketPosition == MarketPosition.Long)
						? Close[0] - (LongTrailDistanceTicks * TickSize)
						: Close[0] + (LongTrailDistanceTicks * TickSize);
					SetStopLoss(CalculationMode.Price, trailPrice);
				}
			}
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (SystemPerformance.AllTrades.Count > 0)
			{
				Trade lastTrade = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1];
				dailyCumProfit += lastTrade.ProfitCurrency;

				if (UseDailyPnLLock)
				{
					if (dailyCumProfit <= -Math.Abs(DailyMaxLossDollars) || dailyCumProfit >= DailyProfitTargetDollars)
					{
						dailyPnLocked = true;
					}
				}
			}

			// Pintar marcadores de entrada y salida en velas
			if (execution != null)
			{
				botTradeTagCounter++;
				if (marketPosition == MarketPosition.Long)
				{
					Draw.ArrowUp(this, "EXEC_BUY_" + botTradeTagCounter, false, 0, Low[0] - TickSize * 5, Brushes.Lime);
					Draw.Text(this, "EXEC_BUY_LBL_" + botTradeTagCounter, false, $"▲ COMPRA {quantity}x @{price:F2}", 0, Low[0] - TickSize * 15, 0, Brushes.Lime, new SimpleFont("Arial", 8), TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
				}
				else if (marketPosition == MarketPosition.Short)
				{
					Draw.ArrowDown(this, "EXEC_SELL_" + botTradeTagCounter, false, 0, High[0] + TickSize * 5, Brushes.Red);
					Draw.Text(this, "EXEC_SELL_LBL_" + botTradeTagCounter, false, $"▼ VENTA {quantity}x @{price:F2}", 0, High[0] + TickSize * 15, 0, Brushes.Red, new SimpleFont("Arial", 8), TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
				}
				else if (marketPosition == MarketPosition.Flat)
				{
					Draw.Diamond(this, "EXEC_EXIT_" + botTradeTagCounter, false, 0, High[0] + TickSize * 5, Brushes.Gold);
					Draw.Text(this, "EXEC_EXIT_LBL_" + botTradeTagCounter, false, $"💎 SALIDA DE POSICIÓN @{price:F2}", 0, High[0] + TickSize * 12, 0, Brushes.Gold, new SimpleFont("Arial", 8), TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
				}
			}

			PaintChartSpanish();
		}

		// ═══════════════════════════════════════════════════════════════════
		//  ANÁLISIS DIBUJADO EN VELAS (Chart Painting en Español)
		// ═══════════════════════════════════════════════════════════════════
		private void PaintChartSpanish()
		{
			int timeNow = ToTime(Time[0]);

			// 1. ZONA PRE-MERCADO (Antes de StartTime 09:30)
			if (timeNow < StartTime)
			{
				BackBrushes[0] = BrushPMZone;
				if (High[0] > pmHigh) pmHigh = High[0];
				if (Low[0] < pmLow)   pmLow  = Low[0];

				if (pmHigh != double.MinValue)
				{
					Draw.HorizontalLine(this, "PM_HIGH_SP", pmHigh, new Stroke(Brushes.MediumOrchid, DashStyleHelper.Dash, 1));
					Draw.Text(this, "PM_HIGH_LBL_SP", false, "MÁXIMO PRE-MERCADO", 0, pmHigh, 6, Brushes.MediumOrchid, new SimpleFont("Arial", 7), TextAlignment.Right, Brushes.Transparent, Brushes.Transparent, 0);
				}
				if (pmLow != double.MaxValue)
				{
					Draw.HorizontalLine(this, "PM_LOW_SP", pmLow, new Stroke(Brushes.MediumOrchid, DashStyleHelper.Dash, 1));
					Draw.Text(this, "PM_LOW_LBL_SP", false, "MÍNIMO PRE-MERCADO", 0, pmLow, -6, Brushes.MediumOrchid, new SimpleFont("Arial", 7), TextAlignment.Right, Brushes.Transparent, Brushes.Transparent, 0);
				}
				return;
			}

			// 2. SESIÓN DE TRADING ACTIVA (09:30 - 15:50)
			if (timeNow >= StartTime && timeNow <= EndTime)
			{
				if (timeNow == StartTime)
				{
					Draw.VerticalLine(this, "START_TIME_LINE", 0, new Stroke(Brushes.Cyan, DashStyleHelper.Dash, 2));
					Draw.Text(this, "START_TIME_LBL", false, "⚡ APERTURA DE MERCADO (09:30)", 0, High[0] + TickSize * 10, 0, Brushes.Cyan, new SimpleFont("Arial", 8), TextAlignment.Center, Brushes.Cyan, new SolidColorBrush(Color.FromArgb(60, 0, 200, 255)), 80);
				}

				if (Position.MarketPosition == MarketPosition.Long)
				{
					BackBrushes[0] = BrushInTradeLong;
					double sl = Position.AveragePrice - (LongStopLossTicks * TickSize);
					double tp = Position.AveragePrice + (LongProfitTargetTicks * TickSize);

					Draw.HorizontalLine(this, "LIVE_SL", sl, new Stroke(Brushes.Red, DashStyleHelper.Dot, 2));
					Draw.Text(this, "LIVE_SL_LBL", false, $"🔴 STOP LOSS  {sl:F2}", 0, sl, -8, Brushes.Red, new SimpleFont("Arial", 8), TextAlignment.Right, Brushes.Transparent, Brushes.Transparent, 0);

					Draw.HorizontalLine(this, "LIVE_TP", tp, new Stroke(Brushes.LimeGreen, DashStyleHelper.Dot, 2));
					Draw.Text(this, "LIVE_TP_LBL", false, $"🟢 TAKE PROFIT  {tp:F2}", 0, tp, 8, Brushes.LimeGreen, new SimpleFont("Arial", 8), TextAlignment.Right, Brushes.Transparent, Brushes.Transparent, 0);

					Draw.HorizontalLine(this, "LIVE_ENTRY", Position.AveragePrice, new Stroke(Brushes.White, DashStyleHelper.Solid, 1));
					Draw.Text(this, "LIVE_ENTRY_LBL", false, $"📍 PRECIO ENTRADA  {Position.AveragePrice:F2}", 0, Position.AveragePrice, 0, Brushes.White, new SimpleFont("Arial", 8), TextAlignment.Right, Brushes.Transparent, Brushes.Transparent, 0);
				}
				else if (Position.MarketPosition == MarketPosition.Short)
				{
					BackBrushes[0] = BrushInTradeShort;
					double sl = Position.AveragePrice + (ShortStopLossTicks * TickSize);
					double tp = Position.AveragePrice - (ShortProfitTargetTicks * TickSize);

					Draw.HorizontalLine(this, "LIVE_SL", sl, new Stroke(Brushes.Red, DashStyleHelper.Dot, 2));
					Draw.Text(this, "LIVE_SL_LBL", false, $"🔴 STOP LOSS  {sl:F2}", 0, sl, 8, Brushes.Red, new SimpleFont("Arial", 8), TextAlignment.Right, Brushes.Transparent, Brushes.Transparent, 0);

					Draw.HorizontalLine(this, "LIVE_TP", tp, new Stroke(Brushes.LimeGreen, DashStyleHelper.Dot, 2));
					Draw.Text(this, "LIVE_TP_LBL", false, $"🟢 TAKE PROFIT  {tp:F2}", 0, tp, -8, Brushes.LimeGreen, new SimpleFont("Arial", 8), TextAlignment.Right, Brushes.Transparent, Brushes.Transparent, 0);

					Draw.HorizontalLine(this, "LIVE_ENTRY", Position.AveragePrice, new Stroke(Brushes.White, DashStyleHelper.Solid, 1));
					Draw.Text(this, "LIVE_ENTRY_LBL", false, $"📍 PRECIO ENTRADA  {Position.AveragePrice:F2}", 0, Position.AveragePrice, 0, Brushes.White, new SimpleFont("Arial", 8), TextAlignment.Right, Brushes.Transparent, Brushes.Transparent, 0);
				}
				else
				{
					RemoveDrawObject("LIVE_SL");
					RemoveDrawObject("LIVE_TP");
					RemoveDrawObject("LIVE_ENTRY");
					BackBrushes[0] = BrushActiveZone;
				}
			}

			// 3. FIN DE SESIÓN (15:50 Force Flat)
			if (timeNow >= EndTime)
			{
				Draw.VerticalLine(this, "END_TIME_LINE", 0, new Stroke(Brushes.OrangeRed, DashStyleHelper.Dot, 2));
				Draw.Text(this, "END_TIME_LBL", false, "⚠ FORCE FLAT (15:50)", 0, High[0] + TickSize * 10, 0, Brushes.OrangeRed, new SimpleFont("Arial", 8), TextAlignment.Center, Brushes.OrangeRed, new SolidColorBrush(Color.FromArgb(50, 255, 80, 0)), 80);
				BackBrushes[0] = new SolidColorBrush(Color.FromArgb(20, 150, 150, 150));
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
				Background = new SolidColorBrush(Color.FromArgb(230, 15, 23, 42)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(2, 132, 199)),
				BorderThickness = new Thickness(2),
				CornerRadius = new CornerRadius(10),
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Top,
				Margin = new Thickness(15, 15, 0, 0),
				Padding = new Thickness(12),
				Width = 320
			};

			StackPanel panel = new StackPanel();

			TextBlock headerText = new TextBlock
			{
				Text = "BOT DE APERTURA FUTUROS",
				Foreground = Brushes.White,
				FontWeight = FontWeights.Bold,
				FontSize = 14,
				HorizontalAlignment = HorizontalAlignment.Center,
				Margin = new Thickness(0, 0, 0, 8)
			};
			panel.Children.Add(headerText);

			statusText = new TextBlock
			{
				Text = "ESTADO: ACTIVO / LIVE",
				Foreground = Brushes.LightGreen,
				FontWeight = FontWeights.SemiBold,
				FontSize = 12,
				HorizontalAlignment = HorizontalAlignment.Center,
				Margin = new Thickness(0, 0, 0, 8)
			};
			panel.Children.Add(statusText);

			pnlText = new TextBlock
			{
				Text = "PnL Realizado: $0.00 | Trades: 0",
				Foreground = Brushes.Cyan,
				FontSize = 11,
				HorizontalAlignment = HorizontalAlignment.Center,
				Margin = new Thickness(0, 0, 0, 10)
			};
			panel.Children.Add(pnlText);

			// Botones de Acción
			btnFlatten = new Button
			{
				Content = "🚨 FLATTEN & CANCEL ALL",
				Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)),
				Foreground = Brushes.White,
				FontWeight = FontWeights.Bold,
				Height = 32,
				Margin = new Thickness(0, 4, 0, 4),
				Cursor = System.Windows.Input.Cursors.Hand
			};
			btnFlatten.Click += (s, e) => FlattenAndCancelAll();
			panel.Children.Add(btnFlatten);

			btnPause = new Button
			{
				Content = "⏸️ PAUSE BOT",
				Background = new SolidColorBrush(Color.FromRgb(249, 115, 22)),
				Foreground = Brushes.White,
				FontWeight = FontWeights.Bold,
				Height = 28,
				Margin = new Thickness(0, 2, 0, 2),
				Cursor = System.Windows.Input.Cursors.Hand
			};
			btnPause.Click += (s, e) => TogglePauseBot();
			panel.Children.Add(btnPause);

			btnReset = new Button
			{
				Content = "🟢 RESET PnL",
				Background = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
				Foreground = Brushes.White,
				FontWeight = FontWeights.Bold,
				Height = 28,
				Margin = new Thickness(0, 2, 0, 2),
				Cursor = System.Windows.Input.Cursors.Hand
			};
			btnReset.Click += (s, e) => ResetDailyPnL();
			panel.Children.Add(btnReset);

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

		private void FlattenAndCancelAll()
		{
			if (Position.MarketPosition == MarketPosition.Long) ExitLong();
			else if (Position.MarketPosition == MarketPosition.Short) ExitShort();

			if (Account != null)
			{
				foreach (Order o in Account.Orders)
				{
					if (o.OrderState == OrderState.Working || o.OrderState == OrderState.Submitted)
					{
						Account.Cancel(new[] { o });
					}
				}
			}
			Print("[BOT APERTURA] ¡FLATTEN & CANCEL ALL EJECUTADO!");
		}

		private void TogglePauseBot()
		{
			isPaused = !isPaused;
			if (btnPause != null)
			{
				btnPause.Content = isPaused ? "▶️ RESUME BOT" : "⏸️ PAUSE BOT";
				btnPause.Background = isPaused ? Brushes.Red : new SolidColorBrush(Color.FromRgb(249, 115, 22));
			}
			if (statusText != null)
			{
				statusText.Text = isPaused ? "ESTADO: PAUSADO" : "ESTADO: ACTIVO / LIVE";
				statusText.Foreground = isPaused ? Brushes.OrangeRed : Brushes.LightGreen;
			}
			Print("[BOT APERTURA] Estado de pausa cambiado a: " + isPaused);
		}

		private void ResetDailyPnL()
		{
			dailyCumProfit = 0;
			dailyPnLocked = false;
			tradesTodayCount = 0;
			if (pnlText != null)
			{
				pnlText.Text = "PnL Realizado: $0.00 | Trades: 0";
			}
			Print("[BOT APERTURA] PnL Diario Reiniciado.");
		}
		#endregion
	}
}
