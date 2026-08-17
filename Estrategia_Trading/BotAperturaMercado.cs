#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class BotAperturaMercado : Strategy
    {
        public enum BAM_PerfilCuenta { Cuenta_50K, Cuenta_100K, Cuenta_150K, Personalizado }

        // Campos privados
        private EMA      bam_rapida;
        private EMA      bam_media;
        private double   bam_pnl    = 0;
        private int      bam_ops    = 0;
        private bool     bam_lock   = false;
        private bool     bam_ok     = true;
        private int      bam_tag    = 0;
        private DateTime bam_dia    = DateTime.MinValue;

        // Propiedades con prefijo BAM_ unico
        #region 1_General
        [NinjaScriptProperty]
        [Display(Name = "Perfil de Cuenta", Order = 1, GroupName = "1_General")]
        public BAM_PerfilCuenta BAM_Perfil { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Contratos", Order = 2, GroupName = "1_General")]
        public int BAM_Contratos { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir Longs", Order = 3, GroupName = "1_General")]
        public bool BAM_Long { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir Shorts", Order = 4, GroupName = "1_General")]
        public bool BAM_Short { get; set; }
        #endregion

        #region 2_EMAs
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Rapida Periodo", Order = 1, GroupName = "2_EMAs")]
        public int BAM_EmaR { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Media Periodo", Order = 2, GroupName = "2_EMAs")]
        public int BAM_EmaM { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Filtro Stack EMA", Order = 3, GroupName = "2_EMAs")]
        public bool BAM_Stack { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Filtro Slope EMA", Order = 4, GroupName = "2_EMAs")]
        public bool BAM_Slope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Slope Minimo (Ticks)", Order = 5, GroupName = "2_EMAs")]
        public double BAM_SlopeVal { get; set; }
        #endregion

        #region 3_Filtros
        [NinjaScriptProperty]
        [Display(Name = "Filtro Cuerpo Vela", Order = 1, GroupName = "3_Filtros")]
        public bool BAM_FiltroCuerpo { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cuerpo Min Ticks", Order = 2, GroupName = "3_Filtros")]
        public int BAM_CuerpoMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cuerpo Max Ticks", Order = 3, GroupName = "3_Filtros")]
        public int BAM_CuerpoMax { get; set; }
        #endregion

        #region 4_Horario
        [NinjaScriptProperty]
        [Display(Name = "Hora Inicio HHMMSS", Order = 1, GroupName = "4_Horario")]
        public int BAM_HrIn { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Hora Fin HHMMSS", Order = 2, GroupName = "4_Horario")]
        public int BAM_HrFin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cerrar al Fin Sesion", Order = 3, GroupName = "4_Horario")]
        public bool BAM_CerrarFin { get; set; }
        #endregion

        #region 5_Riesgo
        [NinjaScriptProperty]
        [Display(Name = "Activar Limite PnL Diario", Order = 1, GroupName = "5_Riesgo")]
        public bool BAM_LimitePnL { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Perdida Max Dia ($)", Order = 2, GroupName = "5_Riesgo")]
        public double BAM_PerdMax { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Ganancia Obj Dia ($)", Order = 3, GroupName = "5_Riesgo")]
        public double BAM_GanObj { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Max Trades Dia", Order = 4, GroupName = "5_Riesgo")]
        public int BAM_MaxTrades { get; set; }
        #endregion

        #region 6_Stops
        [NinjaScriptProperty]
        [Display(Name = "SL Long Ticks", Order = 1, GroupName = "6_Stops")]
        public int BAM_SLL { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL Short Ticks", Order = 2, GroupName = "6_Stops")]
        public int BAM_SLS { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TP Long Ticks", Order = 3, GroupName = "6_Stops")]
        public int BAM_TPL { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TP Short Ticks", Order = 4, GroupName = "6_Stops")]
        public int BAM_TPS { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar Breakeven", Order = 5, GroupName = "6_Stops")]
        public bool BAM_BE { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BE Trigger Ticks", Order = 6, GroupName = "6_Stops")]
        public int BAM_BETick { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BE Offset Ticks", Order = 7, GroupName = "6_Stops")]
        public int BAM_BEOfs { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar Trailing Stop", Order = 8, GroupName = "6_Stops")]
        public bool BAM_TS { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TS Trigger Ticks", Order = 9, GroupName = "6_Stops")]
        public int BAM_TSTick { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TS Distancia Ticks", Order = 10, GroupName = "6_Stops")]
        public int BAM_TSDist { get; set; }
        #endregion

        protected override void OnStateChange()
        {
            try
            {
                if (State == State.SetDefaults)
                {
                    Description = "Bot de Apertura de Mercado - NinjaTrader 8";
                    Name        = "BotAperturaMercado";
                    Calculate   = Calculate.OnBarClose;
                    IsInstantiatedOnEachOptimizationProperty = true;
                    IsExitOnSessionCloseStrategy  = true;
                    ExitOnSessionCloseSeconds     = 30;
                    BarsRequiredToTrade           = 20;

                    BAM_Perfil    = BAM_PerfilCuenta.Cuenta_50K;
                    BAM_Contratos = 1;
                    BAM_Long      = true;
                    BAM_Short     = true;
                    BAM_EmaR      = 9;
                    BAM_EmaM      = 21;
                    BAM_Stack     = true;
                    BAM_Slope     = false;
                    BAM_SlopeVal  = 0.5;
                    BAM_FiltroCuerpo = true;
                    BAM_CuerpoMin = 4;
                    BAM_CuerpoMax = 40;
                    BAM_HrIn      = 93000;
                    BAM_HrFin     = 155000;
                    BAM_CerrarFin = true;
                    BAM_LimitePnL = true;
                    BAM_PerdMax   = 500;
                    BAM_GanObj    = 1000;
                    BAM_MaxTrades = 5;
                    BAM_SLL       = 30;
                    BAM_SLS       = 30;
                    BAM_TPL       = 60;
                    BAM_TPS       = 60;
                    BAM_BE        = true;
                    BAM_BETick    = 20;
                    BAM_BEOfs     = 2;
                    BAM_TS        = true;
                    BAM_TSTick    = 30;
                    BAM_TSDist    = 15;
                }
                else if (State == State.Configure)
                {
                    if (BAM_Perfil == BAM_PerfilCuenta.Cuenta_50K)
                    { BAM_Contratos = 2; BAM_PerdMax = 500; BAM_GanObj = 1000; BAM_SLL = 30; BAM_SLS = 30; BAM_TPL = 60; BAM_TPS = 60; }
                    else if (BAM_Perfil == BAM_PerfilCuenta.Cuenta_100K)
                    { BAM_Contratos = 5; BAM_PerdMax = 1000; BAM_GanObj = 2000; BAM_SLL = 40; BAM_SLS = 40; BAM_TPL = 80; BAM_TPS = 80; }
                    else if (BAM_Perfil == BAM_PerfilCuenta.Cuenta_150K)
                    { BAM_Contratos = 15; BAM_PerdMax = 1500; BAM_GanObj = 3000; BAM_SLL = 103; BAM_SLS = 103; BAM_TPL = 384; BAM_TPS = 384; }

                    if (BAM_SLL > 0) SetStopLoss(CalculationMode.Ticks, BAM_SLL);
                    if (BAM_TPL > 0) SetProfitTarget(CalculationMode.Ticks, BAM_TPL);
                }
                else if (State == State.DataLoaded)
                {
                    bam_rapida = EMA(BAM_EmaR);
                    bam_media  = EMA(BAM_EmaM);
                    Print("[BAM] Estrategia cargada correctamente.");
                }
            }
            catch (Exception ex)
            {
                Print("[BAM OnStateChange ERROR] " + ex.Message);
            }
        }

        protected override void OnBarUpdate()
        {
            try
            {
                if (Bars == null || CurrentBar < BarsRequiredToTrade) return;
                if (!bam_ok) return;
                if (bam_rapida == null || bam_media == null) return;

                // Auxiliar para posicion segura (evita NullReferenceException en Position)
                MarketPosition pos = (Position != null) ? Position.MarketPosition : MarketPosition.Flat;
                double avgPrice    = (Position != null) ? Position.AveragePrice : 0;

                // Reset diario
                if (Time != null && Time[0] != null && Time[0].Date != bam_dia)
                {
                    bam_dia  = Time[0].Date;
                    bam_pnl  = 0;
                    bam_ops  = 0;
                    bam_lock = false;
                }

                // Pintar grafico (seguro)
                BAM_Pintar(pos, avgPrice);

                if (BAM_LimitePnL && bam_lock) return;

                int hr = ToTime(Time[0]);
                if (hr < BAM_HrIn || hr > BAM_HrFin)
                {
                    if (BAM_CerrarFin && pos != MarketPosition.Flat)
                    {
                        if (pos == MarketPosition.Long) ExitLong();
                        else if (pos == MarketPosition.Short) ExitShort();
                    }
                    return;
                }

                if (bam_ops >= BAM_MaxTrades) return;

                double cuerpo = Math.Abs(Close[0] - Open[0]) / TickSize;
                double slope  = CurrentBar > 0 ? (bam_media[0] - bam_media[1]) / TickSize : 0;

                // LONG
                if (BAM_Long && pos == MarketPosition.Flat)
                {
                    bool stkOk = !BAM_Stack || bam_rapida[0] > bam_media[0];
                    bool slpOk = !BAM_Slope || slope >= BAM_SlopeVal;
                    bool cOk   = !BAM_FiltroCuerpo || (cuerpo >= BAM_CuerpoMin && cuerpo <= BAM_CuerpoMax);
                    bool pull  = Low[0] <= bam_rapida[0] || Low[0] <= bam_media[0];
                    if (stkOk && slpOk && cOk && pull && Close[0] > Open[0])
                    {
                        SetStopLoss(CalculationMode.Ticks, BAM_SLL);
                        SetProfitTarget(CalculationMode.Ticks, BAM_TPL);
                        EnterLong(BAM_Contratos, "BAM_L");
                        bam_ops++;
                    }
                }
                // SHORT
                else if (BAM_Short && pos == MarketPosition.Flat)
                {
                    bool stkOk = !BAM_Stack || bam_rapida[0] < bam_media[0];
                    bool slpOk = !BAM_Slope || slope <= -BAM_SlopeVal;
                    bool cOk   = !BAM_FiltroCuerpo || (cuerpo >= BAM_CuerpoMin && cuerpo <= BAM_CuerpoMax);
                    bool pull  = High[0] >= bam_rapida[0] || High[0] >= bam_media[0];
                    if (stkOk && slpOk && cOk && pull && Close[0] < Open[0])
                    {
                        SetStopLoss(CalculationMode.Ticks, BAM_SLS);
                        SetProfitTarget(CalculationMode.Ticks, BAM_TPS);
                        EnterShort(BAM_Contratos, "BAM_S");
                        bam_ops++;
                    }
                }

                // Breakeven y Trailing
                if (pos != MarketPosition.Flat && avgPrice > 0)
                {
                    double pt = (pos == MarketPosition.Long)
                        ? (Close[0] - avgPrice) / TickSize
                        : (avgPrice - Close[0]) / TickSize;

                    if (BAM_BE && pt >= BAM_BETick)
                        SetStopLoss(CalculationMode.Ticks, -BAM_BEOfs);
                    if (BAM_TS && pt >= BAM_TSTick)
                    {
                        double tp = (pos == MarketPosition.Long)
                            ? Close[0] - BAM_TSDist * TickSize
                            : Close[0] + BAM_TSDist * TickSize;
                        SetStopLoss(CalculationMode.Price, tp);
                    }
                }
            }
            catch (Exception ex)
            {
                Print("[BAM OnBarUpdate ERROR] " + ex.Message);
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition,
            string orderId, DateTime time)
        {
            try
            {
                if (execution == null) return;

                if (SystemPerformance != null && SystemPerformance.AllTrades != null
                    && SystemPerformance.AllTrades.Count > 0)
                {
                    var last = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1];
                    if (last != null)
                    {
                        bam_pnl += last.ProfitCurrency;
                        if (BAM_LimitePnL && (bam_pnl <= -Math.Abs(BAM_PerdMax) || bam_pnl >= BAM_GanObj))
                            bam_lock = true;
                    }
                }

                if (Bars != null && CurrentBar >= 1)
                {
                    bam_tag++;
                    if (marketPosition == MarketPosition.Long)
                        Draw.ArrowUp(this, "B" + bam_tag, false, 0, Low[0] - TickSize * 4, Brushes.Lime);
                    else if (marketPosition == MarketPosition.Short)
                        Draw.ArrowDown(this, "B" + bam_tag, false, 0, High[0] + TickSize * 4, Brushes.Red);
                    else
                        Draw.Diamond(this, "B" + bam_tag, false, 0, High[0] + TickSize * 4, Brushes.Gold);
                }
            }
            catch (Exception ex)
            {
                Print("[BAM Exec ERROR] " + ex.Message);
            }
        }

        private void BAM_Pintar(MarketPosition pos, double avgPrice)
        {
            try
            {
                if (BackBrushes == null || BackBrushes.Length == 0) return;
                if (Bars == null || CurrentBar < 1) return;

                int hr = ToTime(Time[0]);

                if (hr < BAM_HrIn)
                {
                    BackBrushes[0] = Brushes.MediumPurple;
                    return;
                }
                if (hr > BAM_HrFin)
                {
                    BackBrushes[0] = Brushes.Gray;
                    return;
                }

                if (pos == MarketPosition.Long)
                    BackBrushes[0] = Brushes.DarkGreen;
                else if (pos == MarketPosition.Short)
                    BackBrushes[0] = Brushes.DarkRed;
                else
                    BackBrushes[0] = Brushes.DarkSlateBlue;

                if (pos != MarketPosition.Flat && avgPrice > 0)
                {
                    double sl = pos == MarketPosition.Long
                        ? avgPrice - BAM_SLL * TickSize
                        : avgPrice + BAM_SLS * TickSize;
                    double tp = pos == MarketPosition.Long
                        ? avgPrice + BAM_TPL * TickSize
                        : avgPrice - BAM_TPS * TickSize;

                    Draw.HorizontalLine(this, "BAM_SL", sl,
                        new Stroke(Brushes.Red, DashStyleHelper.Dot, 2));
                    Draw.HorizontalLine(this, "BAM_TP", tp,
                        new Stroke(Brushes.LimeGreen, DashStyleHelper.Dot, 2));
                }
                else
                {
                    RemoveDrawObject("BAM_SL");
                    RemoveDrawObject("BAM_TP");
                }
            }
            catch (Exception ex)
            {
                Print("[BAM Pintar ERROR] " + ex.Message);
            }
        }
    }
}
