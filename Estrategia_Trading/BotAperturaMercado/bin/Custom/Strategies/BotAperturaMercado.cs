#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
        private double   bam_pnl        = 0;
        private int      bam_ops        = 0;
        private bool     bam_lock       = false;
        private bool     bam_ok         = true;
        private int      bam_tag        = 0;
        private DateTime bam_dia        = DateTime.MinValue;
        private int      bam_currentStage = 0; // 0: Ninguno, 1: +$320, 2: +$820, 3: +$1050, 4: +$1600
        private double   bam_highWater  = 0;

        // Rango Pre-Mercado (08:00 - 09:30 AM)
        private double   preHigh        = double.MinValue;
        private double   preLow         = double.MaxValue;
        private int      preStartBar    = -1;

        // Elementos UI WPF HUD (Semi-transparente Glassmorphism)
        private Border      hudBorder;
        private TextBlock   statusText;
        private TextBlock   realizedPnlText;
        private TextBlock   openPnlText;
        private TextBlock   tradesTodayText;
        private TextBlock   progressPctText;
        private Border      progressBarFill;
        private Button      btnFlatten;
        private Button      btnPause;
        private Button      btnReset;
        private Button      btnMinimize;
        private Grid        topRow;
        private Grid        midRow;
        private Border      progressBox;
        private Border      pnlBar;
        private Grid        btnGrid;
        private bool        isPaused    = false;
        private bool        isCollapsed = false;

        // Arrastrar ventana
        private Point       dragStart;
        private bool        isDragging = false;

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

        [NinjaScriptProperty]
        [Display(Name = "Activar Escalera de Bloqueo (Profit Lock)", Order = 5, GroupName = "5_Riesgo")]
        public bool BAM_UsarProfitLock { get; set; }
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
                    IsInstantiatedOnEachOptimizationIteration = true;
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
                    BAM_UsarProfitLock = true; // Escalera de proteccion activada
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

                    if (BAM_SLL > 0) SetStopLoss("BAM_L", CalculationMode.Ticks, BAM_SLL, false);
                    if (BAM_TPL > 0) SetProfitTarget("BAM_L", CalculationMode.Ticks, BAM_TPL);
                    if (BAM_SLS > 0) SetStopLoss("BAM_S", CalculationMode.Ticks, BAM_SLS, false);
                    if (BAM_TPS > 0) SetProfitTarget("BAM_S", CalculationMode.Ticks, BAM_TPS);
                }
                else if (State == State.DataLoaded)
                {
                    bam_rapida = EMA(BAM_EmaR);
                    bam_media  = EMA(BAM_EmaM);
                    Print("[BAM] Estrategia cargada correctamente.");

                    if (ChartControl != null && ChartControl.Dispatcher != null)
                    {
                        ChartControl.Dispatcher.BeginInvoke(new Action(() => {
                            if (hudBorder == null) CreateWpfHudPanel();
                            else EnsureHudAttachedToChart();
                        }), DispatcherPriority.Background);
                    }
                }
                else if (State == State.Realtime)
                {
                    if (ChartControl != null && ChartControl.Dispatcher != null)
                    {
                        ChartControl.Dispatcher.BeginInvoke(new Action(() => {
                            if (hudBorder == null) CreateWpfHudPanel();
                            else EnsureHudAttachedToChart();
                        }), DispatcherPriority.Normal);
                    }
                }
                else if (State == State.Terminated)
                {
                    if (ChartControl != null && ChartControl.Dispatcher != null)
                    {
                        ChartControl.Dispatcher.BeginInvoke(new Action(() => { DisposeWpfHudPanel(); }), DispatcherPriority.Normal);
                    }
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
                if (ChartControl != null && ChartControl.Dispatcher != null && State == State.Realtime)
                {
                    ChartControl.Dispatcher.BeginInvoke(new Action(() => {
                        if (hudBorder == null) CreateWpfHudPanel();
                        else EnsureHudAttachedToChart();
                    }), DispatcherPriority.Background);
                }

                if (Bars == null || CurrentBar < BarsRequiredToTrade) return;
                if (!bam_ok || isPaused) return;
                if (bam_rapida == null || bam_media == null) return;

                MarketPosition pos = (Position != null) ? Position.MarketPosition : MarketPosition.Flat;
                double avgPrice    = (Position != null) ? Position.AveragePrice : 0;
                double openPnl     = (pos != MarketPosition.Flat) ? Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]) : 0;
                double totalPnl    = bam_pnl + openPnl;

                // Reset diario
                if (Time != null && CurrentBar >= 0 && Time[0].Date != bam_dia)
                {
                    bam_dia          = Time[0].Date;
                    bam_pnl          = 0;
                    bam_ops          = 0;
                    bam_lock         = false;
                    bam_currentStage = 0;
                    bam_highWater    = 0;
                    preHigh          = double.MinValue;
                    preLow           = double.MaxValue;
                    preStartBar      = -1;
                }

                // EVALUACION DE LA ESCALERA DE PROTECCION DE GANANCIAS (PROFIT LOCK 4 STAGES)
                if (BAM_UsarProfitLock)
                {
                    bam_highWater = Math.Max(bam_highWater, totalPnl);

                    // Stage 4: Si alcanza +$1,800 -> Asegura minimo +$1,600
                    if (bam_highWater >= 1800)
                    {
                        bam_currentStage = 4;
                        if (totalPnl <= 1600 && pos != MarketPosition.Flat)
                        {
                            if (pos == MarketPosition.Long) ExitLong("BAM_L");
                            else if (pos == MarketPosition.Short) ExitShort("BAM_S");
                            bam_lock = true;
                        }
                    }
                    // Stage 3: Si alcanza +$1,150 -> Asegura minimo +$1,050
                    else if (bam_highWater >= 1150)
                    {
                        bam_currentStage = 3;
                        if (totalPnl <= 1050 && pos != MarketPosition.Flat)
                        {
                            if (pos == MarketPosition.Long) ExitLong("BAM_L");
                            else if (pos == MarketPosition.Short) ExitShort("BAM_S");
                            bam_lock = true;
                        }
                    }
                    // Stage 2: Si alcanza +$1,000 -> Asegura minimo +$820
                    else if (bam_highWater >= 1000)
                    {
                        bam_currentStage = 2;
                        if (totalPnl <= 820 && pos != MarketPosition.Flat)
                        {
                            if (pos == MarketPosition.Long) ExitLong("BAM_L");
                            else if (pos == MarketPosition.Short) ExitShort("BAM_S");
                            bam_lock = true;
                        }
                    }
                    // Stage 1: Si alcanza +$600 -> Asegura minimo +$320
                    else if (bam_highWater >= 600)
                    {
                        bam_currentStage = 1;
                        if (totalPnl <= 320 && pos != MarketPosition.Flat)
                        {
                            if (pos == MarketPosition.Long) ExitLong("BAM_L");
                            else if (pos == MarketPosition.Short) ExitShort("BAM_S");
                            bam_lock = true;
                        }
                    }
                }

                if (State == State.Realtime) UpdateWpfHudMetrics();

                int hr = ToTime(Time[0]);

                // Analisis de Rango Pre-Mercado (08:00 AM - 09:29 AM)
                if (hr >= 80000 && hr < BAM_HrIn)
                {
                    if (preStartBar < 0) preStartBar = CurrentBar;
                    preHigh = Math.Max(preHigh, High[0]);
                    preLow  = Math.Min(preLow, Low[0]);

                    if (preHigh > double.MinValue && preLow < double.MaxValue && preStartBar >= 0)
                    {
                        int barsAgo = CurrentBar - preStartBar;
                        Draw.Rectangle(this, "BAM_PreRange_" + bam_dia.ToString("yyyyMMdd"), false, barsAgo, preHigh, 0, preLow, Brushes.MediumPurple, Brushes.MediumPurple, 15);
                    }
                }

                // Pintar Stop Loss y Take Profit solo cuando hay posicion abierta
                BAM_Pintar(pos, avgPrice);

                if (BAM_LimitePnL && bam_lock) return;

                if (hr < BAM_HrIn || hr > BAM_HrFin)
                {
                    if (BAM_CerrarFin && pos != MarketPosition.Flat)
                    {
                        if (pos == MarketPosition.Long) ExitLong("BAM_L");
                        else if (pos == MarketPosition.Short) ExitShort("BAM_S");
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
                        SetStopLoss("BAM_L", CalculationMode.Ticks, BAM_SLL, false);
                        SetProfitTarget("BAM_L", CalculationMode.Ticks, BAM_TPL);
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
                        SetStopLoss("BAM_S", CalculationMode.Ticks, BAM_SLS, false);
                        SetProfitTarget("BAM_S", CalculationMode.Ticks, BAM_TPS);
                        EnterShort(BAM_Contratos, "BAM_S");
                        bam_ops++;
                    }
                }

                if (pos != MarketPosition.Flat && avgPrice > 0)
                {
                    double pt = (pos == MarketPosition.Long)
                        ? (Close[0] - avgPrice) / TickSize
                        : (avgPrice - Close[0]) / TickSize;

                    if (BAM_BE && pt >= BAM_BETick)
                    {
                        double bePrice = (pos == MarketPosition.Long)
                            ? avgPrice + BAM_BEOfs * TickSize
                            : avgPrice - BAM_BEOfs * TickSize;
                        SetStopLoss((pos == MarketPosition.Long) ? "BAM_L" : "BAM_S", CalculationMode.Price, bePrice, false);
                    }
                    if (BAM_TS && pt >= BAM_TSTick)
                    {
                        double tp = (pos == MarketPosition.Long)
                            ? Close[0] - BAM_TSDist * TickSize
                            : Close[0] + BAM_TSDist * TickSize;
                        SetStopLoss((pos == MarketPosition.Long) ? "BAM_L" : "BAM_S", CalculationMode.Price, tp, false);
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

                // DIBUJAR ENTRADAS Y SALIDAS VISUALES CON ETIQUETAS DE ANALISIS
                if (Bars != null && CurrentBar >= 1)
                {
                    bam_tag++;
                    if (marketPosition == MarketPosition.Long)
                    {
                        Draw.ArrowUp(this, "B" + bam_tag, false, 0, Low[0] - TickSize * 4, Brushes.Lime);
                        Draw.Text(this, "BT" + bam_tag, "BUY " + quantity + " @ " + price.ToString("N2"), 0, Low[0] - TickSize * 12, Brushes.Lime);
                    }
                    else if (marketPosition == MarketPosition.Short)
                    {
                        Draw.ArrowDown(this, "B" + bam_tag, false, 0, High[0] + TickSize * 4, Brushes.Red);
                        Draw.Text(this, "BT" + bam_tag, "SELL " + quantity + " @ " + price.ToString("N2"), 0, High[0] + TickSize * 12, Brushes.Red);
                    }
                    else
                    {
                        Draw.Diamond(this, "B" + bam_tag, false, 0, High[0] + TickSize * 4, Brushes.Gold);
                        Draw.Text(this, "BT" + bam_tag, "EXIT @ " + price.ToString("N2"), 0, High[0] + TickSize * 12, Brushes.Gold);
                    }
                }

                if (State == State.Realtime) UpdateWpfHudMetrics();
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
                if (Bars == null || CurrentBar < 1) return;

                if (pos != MarketPosition.Flat && avgPrice > 0)
                {
                    double sl = pos == MarketPosition.Long
                        ? avgPrice - BAM_SLL * TickSize
                        : avgPrice + BAM_SLS * TickSize;
                    double tp = pos == MarketPosition.Long
                        ? avgPrice + BAM_TPL * TickSize
                        : avgPrice - BAM_TPS * TickSize;

                    Draw.HorizontalLine(this, "BAM_SL", sl, Brushes.Red);
                    Draw.HorizontalLine(this, "BAM_TP", tp, Brushes.LimeGreen);
                    Draw.Text(this, "BAM_SL_TXT", "SL: $" + sl.ToString("N2"), 0, sl, Brushes.Red);
                    Draw.Text(this, "BAM_TP_TXT", "TP: $" + tp.ToString("N2"), 0, tp, Brushes.LimeGreen);
                }
                else
                {
                    RemoveDrawObject("BAM_SL");
                    RemoveDrawObject("BAM_TP");
                    RemoveDrawObject("BAM_SL_TXT");
                    RemoveDrawObject("BAM_TP_TXT");
                }
            }
            catch (Exception ex)
            {
                Print("[BAM Pintar ERROR] " + ex.Message);
            }
        }

        #region WPF HUD Control Panel - Glassmorphism Semi-Transparente
        private static SolidColorBrush HexColor(string hex, byte alpha = 255)
        {
            try
            {
                hex = hex.TrimStart('#');
                if (hex.Length == 6)
                {
                    byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                    byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                    byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                    return new SolidColorBrush(Color.FromArgb(alpha, r, g, b));
                }
            }
            catch {}
            return new SolidColorBrush(Color.FromArgb(alpha, 30, 41, 59));
        }

        private Border CreateSectionBox(string titleText, string headerHex, UIElement contentGrid)
        {
            Border b = new Border
            {
                BorderBrush = HexColor(headerHex, 220),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(6),
                Background = HexColor("#040C18", 170),
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(8)
            };

            StackPanel sp = new StackPanel();
            TextBlock header = new TextBlock
            {
                Text = titleText,
                Foreground = HexColor(headerHex),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6)
            };
            sp.Children.Add(header);
            sp.Children.Add(contentGrid);
            b.Child = sp;
            return b;
        }

        private UIElement CreateKvRow(string labelText, string valText, string valColorHex)
        {
            Grid g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock lbl = new TextBlock
            {
                Text = labelText,
                Foreground = Brushes.White,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0);

            Border valBox = new Border
            {
                Background = HexColor("#020710", 210),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3, 6, 3),
                HorizontalAlignment = HorizontalAlignment.Right,
                MinWidth = 90
            };
            TextBlock val = new TextBlock
            {
                Text = valText,
                Foreground = HexColor(valColorHex),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            valBox.Child = val;
            Grid.SetColumn(valBox, 1);

            g.Children.Add(lbl);
            g.Children.Add(valBox);
            return g;
        }

        private void CreateWpfHudPanel()
        {
            try
            {
                if (ChartControl == null) return;
                if (hudBorder != null)
                {
                    EnsureHudAttachedToChart();
                    return;
                }

                hudBorder = new Border
                {
                    Background = HexColor("#06101E", 200),
                    BorderBrush = HexColor("#0284C7", 230),
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(10),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(12, 12, 0, 0),
                    Padding = new Thickness(12),
                    Width = 480,
                    IsHitTestVisible = true
                };

                StackPanel mainStack = new StackPanel();

                // 1. Header Bar: Title + Drag Handles + Status Pill + Minimize Toggle Button
                Grid headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 10), Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                headerGrid.MouseLeftButtonDown += (s, e) => {
                    isDragging = true;
                    dragStart = e.GetPosition(hudBorder);
                    headerGrid.CaptureMouse();
                };
                headerGrid.MouseMove += (s, e) => {
                    if (isDragging && hudBorder != null) {
                        Point currentPos = e.GetPosition(hudBorder.Parent as UIElement);
                        hudBorder.Margin = new Thickness(
                            Math.Max(0, currentPos.X - dragStart.X),
                            Math.Max(0, currentPos.Y - dragStart.Y),
                            0, 0
                        );
                    }
                };
                headerGrid.MouseLeftButtonUp += (s, e) => {
                    isDragging = false;
                    headerGrid.ReleaseMouseCapture();
                };

                TextBlock title = new TextBlock
                {
                    Text = "⚡ FUTURES MARKET OPENING BOT ✥",
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(title, 0);

                Border statusPill = new Border
                {
                    Background = HexColor("#052E16", 210),
                    BorderBrush = HexColor("#10B981"),
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(0, 0, 6, 0)
                };
                statusText = new TextBlock
                {
                    Text = "STATE: ACTIVE / LIVE",
                    Foreground = HexColor("#10B981"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5
                };
                statusPill.Child = statusText;
                Grid.SetColumn(statusPill, 1);

                btnMinimize = new Button
                {
                    Content = " ➖ ",
                    Foreground = Brushes.White,
                    Background = HexColor("#1E293B", 220),
                    BorderThickness = new Thickness(0),
                    Width = 26,
                    Height = 24,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Cursor = Cursors.Hand,
                    ToolTip = "Minimizar / Expandir Interfaz"
                };
                btnMinimize.Click += (s, e) => {
                    isCollapsed = !isCollapsed;
                    if (topRow != null) topRow.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                    if (midRow != null) midRow.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                    if (progressBox != null) progressBox.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                    if (pnlBar != null) pnlBar.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                    if (btnGrid != null) btnGrid.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                    btnMinimize.Content = isCollapsed ? " ➕ " : " ➖ ";
                    hudBorder.Width = isCollapsed ? 380 : 480;
                };
                Grid.SetColumn(btnMinimize, 2);

                headerGrid.Children.Add(title);
                headerGrid.Children.Add(statusPill);
                headerGrid.Children.Add(btnMinimize);
                mainStack.Children.Add(headerGrid);

                // 2. Top Row (Section 1 + Section 2)
                topRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12, GridUnitType.Pixel) });
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Section 1: Scheduled Opening Entry
                StackPanel sec1Stack = new StackPanel();
                sec1Stack.Children.Add(CreateKvRow("NY Entry Time:", "09:30:00", "#FFFFFF"));
                sec1Stack.Children.Add(CreateKvRow("Entry Window:", "2s", "#FFFFFF"));
                sec1Stack.Children.Add(CreateKvRow("Contracts:", BAM_Contratos.ToString(), "#FFFFFF"));
                sec1Stack.Children.Add(CreateKvRow("Risk:", "$" + BAM_PerdMax.ToString("N0"), "#FFFFFF"));
                sec1Stack.Children.Add(CreateKvRow("Target:", "$" + BAM_GanObj.ToString("N0"), "#FFFFFF"));
                Border sec1 = CreateSectionBox("1. SCHEDULED OPENING ENTRY", "#38BDF8", sec1Stack);
                Grid.SetColumn(sec1, 0);

                // Section 2: Risk Management & PnL
                StackPanel sec2Stack = new StackPanel();
                sec2Stack.Children.Add(CreateKvRow("Stop Loss:", BAM_SLL.ToString() + " ticks", "#FFFFFF"));
                sec2Stack.Children.Add(CreateKvRow("Take Profit:", BAM_TPL.ToString() + " ticks", "#FFFFFF"));
                sec2Stack.Children.Add(CreateKvRow("Max Daily Loss:", "-$" + BAM_PerdMax.ToString("N0"), "#EF4444"));
                sec2Stack.Children.Add(CreateKvRow("Daily Target:", "+$" + BAM_GanObj.ToString("N0"), "#10B981"));
                sec2Stack.Children.Add(CreateKvRow("Force Flat Time:", "15:50:00", "#FFFFFF"));
                Border sec2 = CreateSectionBox("2. RISK MANAGEMENT & PnL", "#FB923C", sec2Stack);
                Grid.SetColumn(sec2, 2);

                topRow.Children.Add(sec1);
                topRow.Children.Add(sec2);
                mainStack.Children.Add(topRow);

                // 3. Middle Row (Section 3 + Section 4)
                midRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                midRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                midRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12, GridUnitType.Pixel) });
                midRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Section 3: Profit Lock (4 Stages) - ESCALERA DE BLOQUEO DE GANANCIAS
                StackPanel sec3Stack = new StackPanel();
                sec3Stack.Children.Add(CreateKvRow("Stage 1:", "$600 → $320", "#FFFFFF"));
                sec3Stack.Children.Add(CreateKvRow("Stage 2:", "$1,000 → $820", "#FFFFFF"));
                sec3Stack.Children.Add(CreateKvRow("Stage 3:", "$1,150 → $1,050", "#FFFFFF"));
                sec3Stack.Children.Add(CreateKvRow("Stage 4:", "$1,800 → $1,600", "#FFFFFF"));
                Border sec3 = CreateSectionBox("3. PROFIT LOCK (4 STAGES)", "#34D399", sec3Stack);
                Grid.SetColumn(sec3, 0);

                // Section 4: Pre-Opening Analysis
                StackPanel sec4Stack = new StackPanel();
                sec4Stack.Children.Add(CreateKvRow("Range Threshold:", BAM_CuerpoMax.ToString() + " pts", "#FFFFFF"));
                sec4Stack.Children.Add(CreateKvRow("Shield Status:", "SAFE / PROTECTED", "#10B981"));
                Border sec4 = CreateSectionBox("4. PRE-OPENING ANALYSIS (08:00 - 09:29 AM)", "#A78BFA", sec4Stack);
                Grid.SetColumn(sec4, 2);

                midRow.Children.Add(sec3);
                midRow.Children.Add(sec4);
                mainStack.Children.Add(midRow);

                // 4. NUEVA SECCION: BARRA DE PROGRESO DE OBJETIVO DIARIO (DAILY TARGET PROGRESS BAR)
                StackPanel progressStack = new StackPanel();
                Grid progressHeaderGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                progressHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                progressHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                TextBlock progressTitle = new TextBlock
                {
                    Text = "🎯 PROGRESO DE OBJETIVO DIARIO (TARGET)",
                    Foreground = HexColor("#F59E0B"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5
                };
                Grid.SetColumn(progressTitle, 0);

                progressPctText = new TextBlock
                {
                    Text = "$0.00 / +$" + BAM_GanObj.ToString("N0") + " (0.0%)",
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5
                };
                Grid.SetColumn(progressPctText, 1);

                progressHeaderGrid.Children.Add(progressTitle);
                progressHeaderGrid.Children.Add(progressPctText);
                progressStack.Children.Add(progressHeaderGrid);

                Border progressBarTrack = new Border
                {
                    Height = 14,
                    Background = HexColor("#020710", 220),
                    BorderBrush = HexColor("#1E293B"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(7),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 2, 0, 2)
                };

                progressBarFill = new Border
                {
                    Height = 12,
                    Width = 0,
                    Background = HexColor("#10B981"),
                    CornerRadius = new CornerRadius(6),
                    HorizontalAlignment = HorizontalAlignment.Left
                };

                progressBarTrack.Child = progressBarFill;
                progressStack.Children.Add(progressBarTrack);
                progressBox = CreateSectionBox("PROGRESO DE GANANCIAS DIARIAS", "#F59E0B", progressStack);
                mainStack.Children.Add(progressBox);

                // 5. Live PnL Bar
                pnlBar = new Border
                {
                    Background = HexColor("#030D1A", 200),
                    BorderBrush = HexColor("#1E293B", 220),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 10)
                };
                Grid pnlGrid = new Grid();
                pnlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                pnlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                pnlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                realizedPnlText = new TextBlock
                {
                    Text = "Realized PnL: +$0.00",
                    Foreground = HexColor("#10B981"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetColumn(realizedPnlText, 0);

                openPnlText = new TextBlock
                {
                    Text = "Open PnL: +$0.00",
                    Foreground = HexColor("#10B981"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetColumn(openPnlText, 1);

                tradesTodayText = new TextBlock
                {
                    Text = "Trades Today: 0",
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetColumn(tradesTodayText, 2);

                pnlGrid.Children.Add(realizedPnlText);
                pnlGrid.Children.Add(openPnlText);
                pnlGrid.Children.Add(tradesTodayText);
                pnlBar.Child = pnlGrid;
                mainStack.Children.Add(pnlBar);

                // 6. Action Buttons (3 Buttons Row)
                btnGrid = new Grid();
                btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
                btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
                btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                btnFlatten = new Button
                {
                    Content = "FLATTEN & CANCEL ALL",
                    Background = HexColor("#EF4444", 220),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Height = 36,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                btnFlatten.Click += (s, e) => {
                    try {
                        if (Position != null && Position.MarketPosition != MarketPosition.Flat) {
                            if (Position.MarketPosition == MarketPosition.Long) ExitLong("BAM_L");
                            else if (Position.MarketPosition == MarketPosition.Short) ExitShort("BAM_S");
                        }
                    } catch {}
                };
                Grid.SetColumn(btnFlatten, 0);

                btnPause = new Button
                {
                    Content = "PAUSE BOT",
                    Background = HexColor("#F97316", 220),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Height = 36,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                btnPause.Click += (s, e) => {
                    isPaused = !isPaused;
                    if (statusText != null) {
                        statusText.Text = isPaused ? "STATE: PAUSED" : "STATE: ACTIVE / LIVE";
                        statusText.Foreground = isPaused ? HexColor("#F97316") : HexColor("#10B981");
                    }
                };
                Grid.SetColumn(btnPause, 2);

                btnReset = new Button
                {
                    Content = "RESET PnL",
                    Background = HexColor("#10B981", 220),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Height = 36,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                btnReset.Click += (s, e) => {
                    bam_pnl = 0; bam_ops = 0; bam_lock = false; bam_currentStage = 0; bam_highWater = 0;
                    UpdateWpfHudMetrics();
                };
                Grid.SetColumn(btnReset, 4);

                btnGrid.Children.Add(btnFlatten);
                btnGrid.Children.Add(btnPause);
                btnGrid.Children.Add(btnReset);
                mainStack.Children.Add(btnGrid);

                hudBorder.Child = mainStack;
                EnsureHudAttachedToChart();
            }
            catch (Exception ex)
            {
                Print("[BAM CreateWpfHudPanel ERROR] " + ex.Message);
            }
        }

        private void EnsureHudAttachedToChart()
        {
            if (ChartControl == null || hudBorder == null) return;

            try
            {
                DependencyObject current = ChartControl;
                while (current != null)
                {
                    if (current is Grid)
                    {
                        Grid g = (Grid)current;
                        if (!g.Children.Contains(hudBorder))
                        {
                            g.Children.Add(hudBorder);
                            Print("[BAM HUD] Interfaz anclada al grafico.");
                        }
                        return;
                    }
                    else if (current is Panel)
                    {
                        Panel p = (Panel)current;
                        if (!p.Children.Contains(hudBorder))
                        {
                            p.Children.Add(hudBorder);
                            Print("[BAM HUD] Interfaz anclada al Panel.");
                        }
                        return;
                    }

                    DependencyObject parent = null;
                    if (current is FrameworkElement) parent = ((FrameworkElement)current).Parent;
                    if (parent == null) parent = VisualTreeHelper.GetParent(current);
                    current = parent;
                }
            }
            catch (Exception ex)
            {
                Print("[BAM HUD ERROR] " + ex.Message);
            }
        }

        private void DisposeWpfHudPanel()
        {
            if (hudBorder == null) return;

            try
            {
                if (ChartControl != null)
                {
                    DependencyObject current = ChartControl;
                    while (current != null)
                    {
                        if (current is Grid)
                        {
                            Grid g = (Grid)current;
                            if (g.Children.Contains(hudBorder))
                            {
                                g.Children.Remove(hudBorder);
                            }
                        }
                        else if (current is Panel)
                        {
                            Panel p = (Panel)current;
                            if (p.Children.Contains(hudBorder))
                            {
                                p.Children.Remove(hudBorder);
                            }
                        }
                        DependencyObject parent = null;
                        if (current is FrameworkElement) parent = ((FrameworkElement)current).Parent;
                        if (parent == null) parent = VisualTreeHelper.GetParent(current);
                        current = parent;
                    }
                }
            }
            catch {}

            hudBorder = null;
        }

        private void UpdateWpfHudMetrics()
        {
            if (ChartControl != null && ChartControl.Dispatcher != null)
            {
                ChartControl.Dispatcher.BeginInvoke(new Action(() => {
                    if (realizedPnlText != null)
                        realizedPnlText.Text = "Realized PnL: " + (bam_pnl >= 0 ? "+" : "") + "$" + bam_pnl.ToString("N2");
                    if (tradesTodayText != null)
                        tradesTodayText.Text = "Trades Today: " + bam_ops;
                    if (openPnlText != null && Position != null)
                    {
                        double openPnl = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
                        openPnlText.Text = "Open PnL: " + (openPnl >= 0 ? "+" : "") + "$" + openPnl.ToString("N2");
                    }

                    // Actualizacion en tiempo real de la Escalera Profit Lock y Barra de Progreso
                    if (progressPctText != null && progressBarFill != null)
                    {
                        double targetVal = Math.Max(1, BAM_GanObj);
                        double pct = Math.Max(0, Math.Min(100, (bam_pnl / targetVal) * 100));

                        if (bam_currentStage == 4)
                        {
                            progressPctText.Text = "🔒 LOCK STAGE 4: GLD +$1,600 (Pico: +$" + bam_highWater.ToString("N0") + ")";
                            progressPctText.Foreground = HexColor("#10B981");
                            progressBarFill.Width = 430;
                            progressBarFill.Background = HexColor("#10B981");
                        }
                        else if (bam_currentStage == 3)
                        {
                            progressPctText.Text = "🔒 LOCK STAGE 3: GLD +$1,050 (Pico: +$" + bam_highWater.ToString("N0") + ")";
                            progressPctText.Foreground = HexColor("#10B981");
                            progressBarFill.Width = Math.Max(250, (pct / 100) * 430);
                            progressBarFill.Background = HexColor("#10B981");
                        }
                        else if (bam_currentStage == 2)
                        {
                            progressPctText.Text = "🔒 LOCK STAGE 2: GLD +$820 (Pico: +$" + bam_highWater.ToString("N0") + ")";
                            progressPctText.Foreground = HexColor("#38BDF8");
                            progressBarFill.Width = Math.Max(200, (pct / 100) * 430);
                            progressBarFill.Background = HexColor("#38BDF8");
                        }
                        else if (bam_currentStage == 1)
                        {
                            progressPctText.Text = "🔒 LOCK STAGE 1: GLD +$320 (Pico: +$" + bam_highWater.ToString("N0") + ")";
                            progressPctText.Foreground = HexColor("#F59E0B");
                            progressBarFill.Width = Math.Max(140, (pct / 100) * 430);
                            progressBarFill.Background = HexColor("#F59E0B");
                        }
                        else if (bam_pnl >= BAM_GanObj)
                        {
                            progressPctText.Text = "🎯 TARGET ALCANZADO! (+$" + bam_pnl.ToString("N2") + ")";
                            progressPctText.Foreground = HexColor("#10B981");
                            progressBarFill.Width = 430;
                            progressBarFill.Background = HexColor("#10B981");
                        }
                        else if (bam_pnl < 0)
                        {
                            double lossPct = Math.Min(100, (Math.Abs(bam_pnl) / Math.Max(1, BAM_PerdMax)) * 100);
                            progressPctText.Text = "⚠️ PnL Negativo: -$" + Math.Abs(bam_pnl).ToString("N2") + " (" + lossPct.ToString("F1") + "% Max Loss)";
                            progressPctText.Foreground = HexColor("#EF4444");
                            progressBarFill.Width = Math.Max(10, (lossPct / 100) * 430);
                            progressBarFill.Background = HexColor("#EF4444");
                        }
                        else
                        {
                            progressPctText.Text = "+$" + bam_pnl.ToString("N2") + " / +$" + targetVal.ToString("N0") + " (" + pct.ToString("F1") + "%)";
                            progressPctText.Foreground = Brushes.White;
                            progressBarFill.Width = Math.Max(0, (pct / 100) * 430);
                            progressBarFill.Background = HexColor("#F59E0B");
                        }
                    }
                }), DispatcherPriority.Background);
            }
        }
        #endregion
    }
}
