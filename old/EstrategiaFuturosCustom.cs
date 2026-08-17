#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public class BotAperturaMercadoCustom : Strategy
	{
		#region Private Fields
		private EMA emaFast;
		private EMA emaMid;
		
		private double dailyCumProfit = 0;
		private int tradesTodayCount = 0;
		private DateTime currentTradeDate = DateTime.MinValue;
		private bool dailyPnLocked = false;
		private int barsInPosition = 0;
		#endregion

		#region Properties - Setup & General
		[NinjaScriptProperty]
		[Display(Name = "Contratos / Lotes", Description = "Número de contratos a operar", Order = 1, GroupName = "1. General & Posición")]
		public int Contracts { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Habilitar Compras (Longs)", Description = "Permitir entradas en Long", Order = 2, GroupName = "1. General & Posición")]
		public bool EnableLongs { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Habilitar Ventas (Shorts)", Description = "Permitir entradas en Short", Order = 3, GroupName = "1. General & Posición")]
		public bool EnableShorts { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Deslizamiento (Slippage Ticks)", Description = "Deslizamiento estimado en ticks", Order = 4, GroupName = "1. General & Posición")]
		public int UserSlippageTicks { get; set; }
		#endregion

		#region Properties - EMAs & Indicadores
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
		[Display(Name = "Pendiente Mínima Mid Short (Ticks)", Order = 6, GroupName = "2. Indicadores & Tendencia")]
		public double ShortMinMidSlopeTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Usar Filtro de Apilamiento EMA", Order = 7, GroupName = "2. Indicadores & Tendencia")]
		public bool UseEmaStackFilter { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Permitir Retroceso a Fast", Order = 8, GroupName = "2. Indicadores & Tendencia")]
		public bool AllowPullbackToFast { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Permitir Retroceso a Mid", Order = 9, GroupName = "2. Indicadores & Tendencia")]
		public bool AllowPullbackToMid { get; set; }
		#endregion

		#region Properties - Filtros de Vela & Señal
		[NinjaScriptProperty]
		[Display(Name = "Usar Filtro de Cuerpo de Vela", Order = 1, GroupName = "3. Filtros de Señal")]
		public bool UseBaseBodyFilter { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cuerpo Mínimo de Vela (Ticks)", Order = 2, GroupName = "3. Filtros de Señal")]
		public int BaseMinSignalBodyTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cuerpo Máximo de Vela (Ticks)", Order = 3, GroupName = "3. Filtros de Señal")]
		public int BaseMaxSignalBodyTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Requerir Cierre Más Allá de Fast", Order = 4, GroupName = "3. Filtros de Señal")]
		public bool RequireCloseBeyondFast { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Requerir Ruptura de Vela Previa", Order = 5, GroupName = "3. Filtros de Señal")]
		public bool RequirePriorBarBreak { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Requerir Dirección de Vela de Señal", Order = 6, GroupName = "3. Filtros de Señal")]
		public bool RequireSignalCandleDirection { get; set; }
		#endregion

		#region Properties - Ventanas Horarias
		[NinjaScriptProperty]
		[Display(Name = "Hora Inicio Sesión (HHMMSS)", Order = 1, GroupName = "4. Filtro Horario")]
		public int StartTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Hora Fin Sesión (HHMMSS)", Order = 2, GroupName = "4. Filtro Horario")]
		public int EndTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cerrar Posición al Finalizar Horario", Order = 3, GroupName = "4. Filtro Horario")]
		public bool ExitAfterEndTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Usar Límite Operaciones los Viernes", Order = 4, GroupName = "4. Filtro Horario")]
		public bool UseFridayThrottle { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Máximo Operaciones los Viernes", Order = 5, GroupName = "4. Filtro Horario")]
		public int FridayMaxTrades { get; set; }
		#endregion

		#region Properties - Gestión de Riesgo Diario
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
		[Display(Name = "Cerrar Posición al Tocar Límite Diario", Order = 4, GroupName = "5. Control de Riesgo Diario")]
		public bool FlattenOpenPositionOnDailyPnLHit { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Usar Máximo de Operaciones Diarias", Order = 5, GroupName = "5. Control de Riesgo Diario")]
		public bool UseMaxTradesPerDay { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Máximo Operaciones por Día", Order = 6, GroupName = "5. Control de Riesgo Diario")]
		public int MaxTradesPerDay { get; set; }
		#endregion

		#region Properties - Stops & Take Profit
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

		#region Properties - Escalera de Ganancias (Profit Protection Ladder)
		[NinjaScriptProperty]
		[Display(Name = "Usar Escalera de Protección de Ganancias", Order = 1, GroupName = "7. Profit Protection Ladder")]
		public bool UseProfitProtectionLadder { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Lock Nivel 1 Trigger (Ticks)", Order = 2, GroupName = "7. Profit Protection Ladder")]
		public int Lock1TriggerTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Lock Nivel 1 Ganancia (Ticks)", Order = 3, GroupName = "7. Profit Protection Ladder")]
		public int Lock1ProfitTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Lock Nivel 2 Trigger (Ticks)", Order = 4, GroupName = "7. Profit Protection Ladder")]
		public int Lock2TriggerTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Lock Nivel 2 Ganancia (Ticks)", Order = 5, GroupName = "7. Profit Protection Ladder")]
		public int Lock2ProfitTicks { get; set; }
		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description							= "Estrategia automatizada de trading de futuros para apertura de mercado.";
				Name								= "BotAperturaMercadoCustom";
				Calculate							= Calculate.OnBarClose;
				IsInstantiatedOnEachOptimizationProperty = true;
				IsExitOnSessionCloseStrategy		= true;
				ExitOnSessionCloseSeconds			= 30;
				BarsRequiredToTrade					= 20;

				// Valores por defecto
				Contracts							= 1;
				EnableLongs							= true;
				EnableShorts						= true;
				UserSlippageTicks					= 1;

				FastPeriod							= 9;
				MidPeriod							= 21;
				ShowEMAs							= true;
				UseEmaSlopeFilter					= true;
				LongMinMidSlopeTicks				= 0.5;
				ShortMinMidSlopeTicks				= 0.5;
				UseEmaStackFilter					= true;
				AllowPullbackToFast					= true;
				AllowPullbackToMid					= true;

				UseBaseBodyFilter					= true;
				BaseMinSignalBodyTicks				= 4;
				BaseMaxSignalBodyTicks				= 40;
				RequireCloseBeyondFast				= true;
				RequirePriorBarBreak				= true;
				RequireSignalCandleDirection		= true;

				StartTime							= 93000;  // 09:30:00 EST
				EndTime								= 154500; // 15:45:00 EST
				ExitAfterEndTime					= true;
				UseFridayThrottle					= true;
				FridayMaxTrades						= 3;

				UseDailyPnLLock						= true;
				DailyMaxLossDollars					= 500;
				DailyProfitTargetDollars			= 1000;
				FlattenOpenPositionOnDailyPnLHit	= true;
				UseMaxTradesPerDay					= true;
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

				UseProfitProtectionLadder			= true;
				Lock1TriggerTicks					= 25;
				Lock1ProfitTicks					= 10;
				Lock2TriggerTicks					= 40;
				Lock2ProfitTicks					= 25;
			}
			else if (State == State.Configure)
			{
				SetStopLoss(CalculationMode.Ticks, LongStopLossTicks);
				SetProfitTarget(CalculationMode.Ticks, LongProfitTargetTicks);
			}
			else if (State == State.DataLoaded)
			{
				emaFast = EMA(FastPeriod);
				emaMid = EMA(MidPeriod);

				if (ShowEMAs)
				{
					AddChartIndicator(emaFast);
					AddChartIndicator(emaMid);
				}
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < BarsRequiredToTrade)
				return;

			// Restablecimiento de métricas al cambiar de día
			if (Time[0].Date != currentTradeDate)
			{
				currentTradeDate = Time[0].Date;
				dailyCumProfit = 0;
				tradesTodayCount = 0;
				dailyPnLocked = false;
			}

			// Verificación de Bloqueo Diario de PnL
			if (UseDailyPnLLock && dailyPnLocked)
			{
				if (Position.MarketPosition != MarketPosition.Flat && FlattenOpenPositionOnDailyPnLHit)
				{
					if (Position.MarketPosition == MarketPosition.Long) ExitLong();
					else if (Position.MarketPosition == MarketPosition.Short) ExitShort();
				}
				return;
			}

			// Filtro Horario
			int timeNow = ToTime(Time[0]);
			if (timeNow < StartTime || timeNow > EndTime)
			{
				if (ExitAfterEndTime && Position.MarketPosition != MarketPosition.Flat)
				{
					if (Position.MarketPosition == MarketPosition.Long) ExitLong();
					else if (Position.MarketPosition == MarketPosition.Short) ExitShort();
				}
				return;
			}

			// Filtro de Operaciones Máximas por Día
			if (UseMaxTradesPerDay && tradesTodayCount >= MaxTradesPerDay)
				return;

			if (UseFridayThrottle && Time[0].DayOfWeek == DayOfWeek.Friday && tradesTodayCount >= FridayMaxTrades)
				return;

			// Cálculo de Parámetros de Vela
			double candleBodyTicks = Math.Abs(Close[0] - Open[0]) / TickSize;
			double midSlope = (emaMid[0] - emaMid[1]) / TickSize;

			// ==========================================
			// LÓGICA DE ENTRADA LONG (COMPRA)
			// ==========================================
			if (EnableLongs && Position.MarketPosition == MarketPosition.Flat)
			{
				bool emaTrendOk = !UseEmaStackFilter || (emaFast[0] > emaMid[0]);
				bool slopeOk = !UseEmaSlopeFilter || (midSlope >= LongMinMidSlopeTicks);
				bool bodyOk = !UseBaseBodyFilter || (candleBodyTicks >= BaseMinSignalBodyTicks && candleBodyTicks <= BaseMaxSignalBodyTicks);
				bool closeBeyondOk = !RequireCloseBeyondFast || (Close[0] > emaFast[0]);
				bool priorBreakOk = !RequirePriorBarBreak || (High[0] > High[1]);
				bool candleDirOk = !RequireSignalCandleDirection || (Close[0] > Open[0]);

				// Condición de Pullback (Retroceso) a la EMA rápida/media con rebote a favor
				bool pullbackOk = (Low[0] <= emaFast[0] || Low[0] <= emaMid[0]);

				if (emaTrendOk && slopeOk && bodyOk && closeBeyondOk && priorBreakOk && candleDirOk && pullbackOk)
				{
					SetStopLoss(CalculationMode.Ticks, LongStopLossTicks);
					SetProfitTarget(CalculationMode.Ticks, LongProfitTargetTicks);
					EnterLong(Contracts, "Moonbound_Long");
					tradesTodayCount++;
					barsInPosition = 0;
				}
			}

			// ==========================================
			// LÓGICA DE ENTRADA SHORT (VENTA)
			// ==========================================
			else if (EnableShorts && Position.MarketPosition == MarketPosition.Flat)
			{
				bool emaTrendOk = !UseEmaStackFilter || (emaFast[0] < emaMid[0]);
				bool slopeOk = !UseEmaSlopeFilter || (midSlope <= -ShortMinMidSlopeTicks);
				bool bodyOk = !UseBaseBodyFilter || (candleBodyTicks >= BaseMinSignalBodyTicks && candleBodyTicks <= BaseMaxSignalBodyTicks);
				bool closeBeyondOk = !RequireCloseBeyondFast || (Close[0] < emaFast[0]);
				bool priorBreakOk = !RequirePriorBarBreak || (Low[0] < Low[1]);
				bool candleDirOk = !RequireSignalCandleDirection || (Close[0] < Open[0]);

				bool pullbackOk = (High[0] >= emaFast[0] || High[0] >= emaMid[0]);

				if (emaTrendOk && slopeOk && bodyOk && closeBeyondOk && priorBreakOk && candleDirOk && pullbackOk)
				{
					SetStopLoss(CalculationMode.Ticks, ShortStopLossTicks);
					SetProfitTarget(CalculationMode.Ticks, ShortProfitTargetTicks);
					EnterShort(Contracts, "Moonbound_Short");
					tradesTodayCount++;
					barsInPosition = 0;
				}
			}

			// ==========================================
			// GESTIÓN DINÁMICA DE POSICIÓN ABIERTA
			// ==========================================
			if (Position.MarketPosition != MarketPosition.Flat)
			{
				barsInPosition++;
				double openProfitTicks = 0;

				if (Position.MarketPosition == MarketPosition.Long)
				{
					openProfitTicks = (Close[0] - Position.AveragePrice) / TickSize;

					// Profit Protection Ladder (Escalera de Bloqueo)
					if (UseProfitProtectionLadder)
					{
						if (openProfitTicks >= Lock2TriggerTicks)
							SetStopLoss(CalculationMode.Ticks, -Lock2ProfitTicks);
						else if (openProfitTicks >= Lock1TriggerTicks)
							SetStopLoss(CalculationMode.Ticks, -Lock1ProfitTicks);
					}
					// Breakeven Estándar
					else if (UseBreakeven && openProfitTicks >= LongBreakevenTriggerTicks)
					{
						SetStopLoss(CalculationMode.Ticks, -LongBreakevenPlusTicks);
					}

					// Trailing Stop Dinámico
					if (UseTrailingStop && openProfitTicks >= LongTrailTriggerTicks)
					{
						double trailStopPrice = Close[0] - (LongTrailDistanceTicks * TickSize);
						SetStopLoss(CalculationMode.Price, trailStopPrice);
					}
				}
				else if (Position.MarketPosition == MarketPosition.Short)
				{
					openProfitTicks = (Position.AveragePrice - Close[0]) / TickSize;

					if (UseProfitProtectionLadder)
					{
						if (openProfitTicks >= Lock2TriggerTicks)
							SetStopLoss(CalculationMode.Ticks, -Lock2ProfitTicks);
						else if (openProfitTicks >= Lock1TriggerTicks)
							SetStopLoss(CalculationMode.Ticks, -Lock1ProfitTicks);
					}
					else if (UseBreakeven && openProfitTicks >= LongBreakevenTriggerTicks)
					{
						SetStopLoss(CalculationMode.Ticks, -LongBreakevenPlusTicks);
					}

					if (UseTrailingStop && openProfitTicks >= LongTrailTriggerTicks)
					{
						double trailStopPrice = Close[0] + (LongTrailDistanceTicks * TickSize);
						SetStopLoss(CalculationMode.Price, trailStopPrice);
					}
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
		}
	}
}
