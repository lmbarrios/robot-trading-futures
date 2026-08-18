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
    public class BotScalperFuturos : Strategy
    {
        public enum BSC_PerfilCuenta { AutoDeteccion, Cuenta_50K, Cuenta_100K, Cuenta_150K, Personalizado }

        // Campos privados
        private EMA      bsc_emaFast;
        private EMA      bsc_emaMid;
        private EMA      bsc_emaFilter;
        private double   bsc_pnl          = 0;
        private int      bsc_ops          = 0;
        private bool     bsc_lock         = false;
        private bool     bsc_ok           = true;
        private int      bsc_tag          = 0;
        private DateTime bsc_dia          = DateTime.MinValue;
        private double   bsc_highWater    = 0;

        // VWAP Calculada Dinamicamente (Sin requerir licencia especial)
        private double   cumVolume        = 0;
        private double   cumTypicalVol    = 0;
        private double   currentVwap      = 0;

        // Elementos UI WPF HUD
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

        private Point       dragStart;
        private bool        isDragging = false;

        #region 1_General
        [NinjaScriptProperty]
        [Display(Name = "Perfil de Cuenta", Description = "AutoDeteccion automatica por saldo de cuenta o seleccion manual.", Order = 1, GroupName = "1_General")]
        public BSC_PerfilCuenta BSC_Perfil { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Contratos / Lotes", Order = 2, GroupName = "1_General")]
        public int BSC_Contratos { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir Longs", Order = 3, GroupName = "1_General")]
        public bool BSC_Long { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir Shorts", Order = 4, GroupName = "1_General")]
        public bool BSC_Short { get; set; }
        #endregion

        #region 2_Scalping_Indicadores
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Rapida Periodo", Order = 1, GroupName = "2_Scalping_Indicadores")]
        public int BSC_EmaRapida { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Media Periodo", Order = 2, GroupName = "2_Scalping_Indicadores")]
        public int BSC_EmaMedia { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Filtrar por VWAP (Sesion)", Order = 3, GroupName = "2_Scalping_Indicadores")]
        public bool BSC_UsarVwap { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar Filtro Tendencia EMA 50", Order = 4, GroupName = "2_Scalping_Indicadores")]
        public bool BSC_UsarEmaFiltro { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Filtro Periodo", Order = 5, GroupName = "2_Scalping_Indicadores")]
        public int BSC_EmaFiltro { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Activar Filtro Inclinacion Slope", Order = 6, GroupName = "2_Scalping_Indicadores")]
        public bool BSC_UsarSlopeFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Slope Minimo (Ticks)", Order = 7, GroupName = "2_Scalping_Indicadores")]
        public double BSC_SlopeMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Activar Separacion Minima EMA", Order = 8, GroupName = "2_Scalping_Indicadores")]
        public bool BSC_UsarSeparacion { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Separacion Minima (Ticks)", Order = 9, GroupName = "2_Scalping_Indicadores")]
        public int BSC_SepMin { get; set; }
        #endregion

        #region 3_Filtros_Vela
        [NinjaScriptProperty]
        [Display(Name = "Activar Filtro Cuerpo Vela", Order = 1, GroupName = "3_Filtros_Vela")]
        public bool BSC_FiltroCuerpo { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cuerpo Min Ticks", Order = 2, GroupName = "3_Filtros_Vela")]
        public int BSC_CuerpoMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cuerpo Max Ticks", Order = 3, GroupName = "3_Filtros_Vela")]
        public int BSC_CuerpoMax { get; set; }
        #endregion

        #region 4_Horario_Liquidez
        [NinjaScriptProperty]
        [Display(Name = "Manana Inicio (HHMMSS)", Order = 1, GroupName = "4_Horario_Liquidez")]
        public int BSC_HrIn1 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Manana Fin (HHMMSS)", Order = 2, GroupName = "4_Horario_Liquidez")]
        public int BSC_HrFin1 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Tarde Inicio (HHMMSS)", Order = 3, GroupName = "4_Horario_Liquidez")]
        public int BSC_HrIn2 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Tarde Fin (HHMMSS)", Order = 4, GroupName = "4_Horario_Liquidez")]
        public int BSC_HrFin2 { get; set; }
        #endregion

        #region 5_Riesgo_y_Escudos
        [NinjaScriptProperty]
        [Display(Name = "Perdida Maxima Diaria ($)", Order = 1, GroupName = "5_Riesgo_y_Escudos")]
        public double BSC_PerdMax { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Ganancia Objetivo Diaria ($)", Order = 2, GroupName = "5_Riesgo_y_Escudos")]
        public double BSC_GanObj { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Max Operaciones al Dia", Order = 3, GroupName = "5_Riesgo_y_Escudos")]
        public int BSC_MaxTrades { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Activar Escudo Trailing Drawdown", Order = 4, GroupName = "5_Riesgo_y_Escudos")]
        public bool BSC_UsarEscudoFondeo { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Pico Minimo Escudo ($)", Order = 5, GroupName = "5_Riesgo_y_Escudos")]
        public double BSC_PicoMinimoEscudo { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Retroceso Maximo Escudo ($)", Order = 6, GroupName = "5_Riesgo_y_Escudos")]
        public double BSC_MaxRetrocesoFlotante { get; set; }
        #endregion

        #region 6_Stops_OCO
        [NinjaScriptProperty]
        [Display(Name = "Stop Loss (Ticks)", Order = 1, GroupName = "6_Stops_OCO")]
        public int BSC_SL { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Take Profit (Ticks)", Order = 2, GroupName = "6_Stops_OCO")]
        public int BSC_TP { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar Breakeven Rapido", Order = 3, GroupName = "6_Stops_OCO")]
        public bool BSC_BE { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BE Trigger Ticks", Order = 4, GroupName = "6_Stops_OCO")]
        public int BSC_BETick { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BE Offset Ticks", Order = 5, GroupName = "6_Stops_OCO")]
        public int BSC_BEOfs { get; set; }
        #endregion

        protected override void OnStateChange()
        {
            try
            {
                if (State == State.SetDefaults)
                {
                    Description = "Bot de Scalping Inteligente VWAP + Momentum - NinjaTrader 8";
                    Name        = "BotScalperFuturos";
                    Calculate   = Calculate.OnBarClose;
                    IsInstantiatedOnEachOptimizationIteration = false;
                    IsExitOnSessionCloseStrategy  = true;
                    ExitOnSessionCloseSeconds     = 30;
                    BarsRequiredToTrade           = 15;

                    BSC_Perfil    = BSC_PerfilCuenta.AutoDeteccion;
                    BSC_Contratos = 1;
                    BSC_Long      = true;
                    BSC_Short     = true;

                    BSC_EmaRapida    = 3;
                    BSC_EmaMedia     = 9;
                    BSC_UsarVwap     = true;
                    BSC_UsarEmaFiltro= true;
                    BSC_EmaFiltro    = 50;
                    BSC_UsarSlopeFilter = true;
                    BSC_SlopeMin     = 0.3;
                    BSC_UsarSeparacion = true;
                    BSC_SepMin       = 1;

                    BSC_FiltroCuerpo = true;
                    BSC_CuerpoMin    = 2;
                    BSC_CuerpoMax    = 12;

                    BSC_HrIn1   = 93500;
                    BSC_HrFin1  = 114500;
                    BSC_HrIn2   = 140000;
                    BSC_HrFin2  = 154500;

                    BSC_PerdMax   = 150;
                    BSC_GanObj    = 300;
                    BSC_MaxTrades = 3;
                    BSC_UsarEscudoFondeo     = true;
                    BSC_PicoMinimoEscudo     = 100;
                    BSC_MaxRetrocesoFlotante = 50;

                    BSC_SL        = 8;
                    BSC_TP        = 12;
                    BSC_BE        = true;
                    BSC_BETick    = 5;
                    BSC_BEOfs     = 1;
                }
                else if (State == State.Configure)
                {
                    BSC_AplicarPresetCuenta();

                    if (BSC_SL > 0) SetStopLoss("BSC_L", CalculationMode.Ticks, BSC_SL, false);
                    if (BSC_TP > 0) SetProfitTarget("BSC_L", CalculationMode.Ticks, BSC_TP);
                    if (BSC_SL > 0) SetStopLoss("BSC_S", CalculationMode.Ticks, BSC_SL, false);
                    if (BSC_TP > 0) SetProfitTarget("BSC_S", CalculationMode.Ticks, BSC_TP);
                }
                else if (State == State.DataLoaded)
                {
                    bsc_emaFast   = EMA(BSC_EmaRapida);
                    bsc_emaMid    = EMA(BSC_EmaMedia);
                    bsc_emaFilter = EMA(BSC_EmaFiltro);
                    Print("[BSC SCALPER] Cargado correctamente con Filtro EMA(" + BSC_EmaFiltro + ").");

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
                Print("[BSC OnStateChange ERROR] " + ex.Message);
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

                // CANDADO INTELIGENTE DUAL DE SEGURIDAD (Protege cuenta real en grafico, permite Backtest en Strategy Analyzer)
                if (ChartControl != null && State != State.Realtime) return;

                if (Bars == null || CurrentBar < BarsRequiredToTrade) return;
                if (!bsc_ok || isPaused) return;

                // Calculo Dinamico de VWAP de Sesion
                if (Bars.IsFirstBarOfSession)
                {
                    cumVolume = 0;
                    cumTypicalVol = 0;
                }
                double typicalPrice = (High[0] + Low[0] + Close[0]) / 3.0;
                cumVolume += Volume[0];
                cumTypicalVol += typicalPrice * Volume[0];
                currentVwap = cumVolume > 0 ? (cumTypicalVol / cumVolume) : Close[0];

                MarketPosition pos = (Position != null) ? Position.MarketPosition : MarketPosition.Flat;
                double avgPrice    = (Position != null) ? Position.AveragePrice : 0;
                double openPnl     = (pos != MarketPosition.Flat) ? Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]) : 0;
                double totalPnl    = bsc_pnl + openPnl;

                // Reset diario
                if (Time != null && CurrentBar >= 0 && Time[0].Date != bsc_dia)
                {
                    bsc_dia          = Time[0].Date;
                    bsc_pnl          = 0;
                    bsc_ops          = 0;
                    bsc_lock         = false;
                    bsc_highWater    = 0;
                }

                // ESCUDO DE FONDEO (TRAILING DRAWDOWN GUARD) EN POSICION ABIERTA
                if (pos != MarketPosition.Flat && BSC_UsarEscudoFondeo)
                {
                    if (openPnl > bsc_highWater) bsc_highWater = openPnl;

                    if (bsc_highWater >= BSC_PicoMinimoEscudo && (bsc_highWater - openPnl) >= BSC_MaxRetrocesoFlotante)
                    {
                        if (pos == MarketPosition.Long) ExitLong("BSC_L");
                        else if (pos == MarketPosition.Short) ExitShort("BSC_S");
                        bsc_lock = true;
                        Print("[BSC SCALPER ESCUDO] Liquido posicion por retroceso. Pico: $" + bsc_highWater.ToString("N0") + ", Actual: $" + openPnl.ToString("N0"));
                    }
                }

                if (State == State.Realtime) UpdateWpfHudMetrics();

                BSC_Pintar(pos, avgPrice);

                if (bsc_lock || (BSC_MaxTrades > 0 && bsc_ops >= BSC_MaxTrades) || (BSC_PerdMax > 0 && bsc_pnl <= -Math.Abs(BSC_PerdMax)) || (BSC_GanObj > 0 && bsc_pnl >= BSC_GanObj))
                {
                    return;
                }

                int hr = ToTime(Time[0]);
                bool esHorarioActivo = (hr >= BSC_HrIn1 && hr <= BSC_HrFin1) || (hr >= BSC_HrIn2 && hr <= BSC_HrFin2);

                if (!esHorarioActivo)
                {
                    if (pos != MarketPosition.Flat)
                    {
                        if (pos == MarketPosition.Long) ExitLong("BSC_L");
                        else if (pos == MarketPosition.Short) ExitShort("BSC_S");
                    }
                    return;
                }

                if (bsc_emaFast == null || bsc_emaMid == null) return;

                double cuerpo = Math.Abs(Close[0] - Open[0]) / TickSize;
                double slope  = (bsc_emaFilter != null && CurrentBar >= 2) ? (bsc_emaFilter[0] - bsc_emaFilter[2]) / TickSize : 0;
                double sep    = (bsc_emaFast != null && bsc_emaMid != null) ? Math.Abs(bsc_emaFast[0] - bsc_emaMid[0]) / TickSize : 0;

                // CONDICION LONG (COMPRA SCALPER)
                if (BSC_Long && pos == MarketPosition.Flat)
                {
                    bool vwapOk   = !BSC_UsarVwap || Close[0] > currentVwap;
                    bool filterOk = !BSC_UsarEmaFiltro || (bsc_emaFilter != null && Close[0] > bsc_emaFilter[0]);
                    bool slopeOk  = !BSC_UsarSlopeFilter || slope >= BSC_SlopeMin;
                    bool sepOk    = !BSC_UsarSeparacion || sep >= BSC_SepMin;
                    bool crossOk  = bsc_emaFast[0] > bsc_emaMid[0] && bsc_emaFast[1] <= bsc_emaMid[1];
                    bool cOk      = !BSC_FiltroCuerpo || (cuerpo >= BSC_CuerpoMin && cuerpo <= BSC_CuerpoMax);

                    if (vwapOk && filterOk && slopeOk && sepOk && crossOk && cOk && Close[0] > Open[0])
                    {
                        SetStopLoss("BSC_L", CalculationMode.Ticks, BSC_SL, false);
                        SetProfitTarget("BSC_L", CalculationMode.Ticks, BSC_TP);
                        EnterLong(BSC_Contratos, "BSC_L");
                        bsc_ops++;
                    }
                }
                // CONDICION SHORT (VENTA SCALPER)
                else if (BSC_Short && pos == MarketPosition.Flat)
                {
                    bool vwapOk   = !BSC_UsarVwap || Close[0] < currentVwap;
                    bool filterOk = !BSC_UsarEmaFiltro || (bsc_emaFilter != null && Close[0] < bsc_emaFilter[0]);
                    bool slopeOk  = !BSC_UsarSlopeFilter || slope <= -BSC_SlopeMin;
                    bool sepOk    = !BSC_UsarSeparacion || sep >= BSC_SepMin;
                    bool crossOk  = bsc_emaFast[0] < bsc_emaMid[0] && bsc_emaFast[1] >= bsc_emaMid[1];
                    bool cOk      = !BSC_FiltroCuerpo || (cuerpo >= BSC_CuerpoMin && cuerpo <= BSC_CuerpoMax);

                    if (vwapOk && filterOk && slopeOk && sepOk && crossOk && cOk && Close[0] < Open[0])
                    {
                        SetStopLoss("BSC_S", CalculationMode.Ticks, BSC_SL, false);
                        SetProfitTarget("BSC_S", CalculationMode.Ticks, BSC_TP);
                        EnterShort(BSC_Contratos, "BSC_S");
                        bsc_ops++;
                    }
                }

                // BREAKEVEN RAPIDO
                if (pos != MarketPosition.Flat && avgPrice > 0 && BSC_BE)
                {
                    double pt = (pos == MarketPosition.Long)
                        ? (Close[0] - avgPrice) / TickSize
                        : (avgPrice - Close[0]) / TickSize;

                    if (pt >= BSC_BETick)
                    {
                        double bePrice = (pos == MarketPosition.Long)
                            ? avgPrice + BSC_BEOfs * TickSize
                            : avgPrice - BSC_BEOfs * TickSize;
                        SetStopLoss((pos == MarketPosition.Long) ? "BSC_L" : "BSC_S", CalculationMode.Price, bePrice, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Print("[BSC OnBarUpdate ERROR] " + ex.Message);
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition,
            string orderId, DateTime time)
        {
            try
            {
                if (execution == null) return;

                if (SystemPerformance != null && SystemPerformance.AllTrades != null && SystemPerformance.AllTrades.Count > 0)
                {
                    var last = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1];
                    if (last != null)
                    {
                        bsc_pnl += last.ProfitCurrency;
                        if (BSC_PerdMax > 0 && (bsc_pnl <= -Math.Abs(BSC_PerdMax) || bsc_pnl >= BSC_GanObj))
                            bsc_lock = true;
                    }
                }

                if (Bars != null && CurrentBar >= 1)
                {
                    bsc_tag++;
                    if (marketPosition == MarketPosition.Long)
                    {
                        Draw.ArrowUp(this, "BSC_" + bsc_tag, false, 0, Low[0] - TickSize * 4, Brushes.Cyan);
                    }
                    else if (marketPosition == MarketPosition.Short)
                    {
                        Draw.ArrowDown(this, "BSC_" + bsc_tag, false, 0, High[0] + TickSize * 4, Brushes.Magenta);
                    }
                }

                if (State == State.Realtime) UpdateWpfHudMetrics();
            }
            catch (Exception ex)
            {
                Print("[BSC Exec ERROR] " + ex.Message);
            }
        }

        private void BSC_AplicarPresetCuenta()
        {
            try
            {
                BSC_PerfilCuenta perfil = BSC_Perfil;

                if (perfil == BSC_PerfilCuenta.AutoDeteccion)
                {
                    double cash = 50000;
                    string name = "";
                    if (Account != null)
                    {
                        try { cash = Account.Get(AccountItem.CashValue, Currency.UsDollar); } catch {}
                        if (Account.Name != null) name = Account.Name.ToUpper();
                    }

                    if (cash >= 140000 || name.Contains("150K") || name.Contains("150000")) perfil = BSC_PerfilCuenta.Cuenta_150K;
                    else if (cash >= 90000 || name.Contains("100K") || name.Contains("100000")) perfil = BSC_PerfilCuenta.Cuenta_100K;
                    else perfil = BSC_PerfilCuenta.Cuenta_50K;
                }

                if (perfil == BSC_PerfilCuenta.Cuenta_50K)
                { BSC_Contratos = 1; BSC_PerdMax = 150; BSC_GanObj = 300; BSC_SL = 8; BSC_TP = 12; }
                else if (perfil == BSC_PerfilCuenta.Cuenta_100K)
                { BSC_Contratos = 2; BSC_PerdMax = 300; BSC_GanObj = 600; BSC_SL = 8; BSC_TP = 12; }
                else if (perfil == BSC_PerfilCuenta.Cuenta_150K)
                { BSC_Contratos = 3; BSC_PerdMax = 450; BSC_GanObj = 900; BSC_SL = 8; BSC_TP = 12; }
            }
            catch {}
        }

        private void BSC_Pintar(MarketPosition pos, double avgPrice)
        {
            try
            {
                if (Bars == null || CurrentBar < 1) return;

                if (pos != MarketPosition.Flat && avgPrice > 0)
                {
                    double sl = pos == MarketPosition.Long ? avgPrice - BSC_SL * TickSize : avgPrice + BSC_SL * TickSize;
                    double tp = pos == MarketPosition.Long ? avgPrice + BSC_TP * TickSize : avgPrice - BSC_TP * TickSize;

                    Draw.HorizontalLine(this, "BSC_SL", sl, Brushes.Red);
                    Draw.HorizontalLine(this, "BSC_TP", tp, Brushes.LimeGreen);
                }
                else
                {
                    RemoveDrawObject("BSC_SL");
                    RemoveDrawObject("BSC_TP");
                }
            }
            catch {}
        }

        #region WPF HUD Control Panel
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

            TextBlock lbl = new TextBlock { Text = labelText, Foreground = Brushes.White, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lbl, 0);

            Border valBox = new Border
            {
                Background = HexColor("#020710", 210),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3, 6, 3),
                HorizontalAlignment = HorizontalAlignment.Right,
                MinWidth = 90
            };
            TextBlock val = new TextBlock { Text = valText, Foreground = HexColor(valColorHex), FontWeight = FontWeights.Bold, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
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
                    Width = 460,
                    IsHitTestVisible = true
                };

                StackPanel mainStack = new StackPanel();

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

                TextBlock title = new TextBlock { Text = "⚡ SCALPER BOT VWAP + MOMENTUM ✥", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
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
                statusText = new TextBlock { Text = "STATE: ACTIVE / LIVE", Foreground = HexColor("#10B981"), FontWeight = FontWeights.Bold, FontSize = 10.5 };
                statusPill.Child = statusText;
                Grid.SetColumn(statusPill, 1);

                btnMinimize = new Button { Content = " ➖ ", Foreground = Brushes.White, Background = HexColor("#1E293B", 220), BorderThickness = new Thickness(0), Width = 26, Height = 24, FontWeight = FontWeights.Bold, FontSize = 11, Cursor = Cursors.Hand };
                btnMinimize.Click += (s, e) => {
                    isCollapsed = !isCollapsed;
                    if (topRow != null) topRow.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                    if (midRow != null) midRow.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                    if (progressBox != null) progressBox.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                    if (pnlBar != null) pnlBar.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                    if (btnGrid != null) btnGrid.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                    btnMinimize.Content = isCollapsed ? " ➕ " : " ➖ ";
                    hudBorder.Width = isCollapsed ? 360 : 460;
                };
                Grid.SetColumn(btnMinimize, 2);

                headerGrid.Children.Add(title); headerGrid.Children.Add(statusPill); headerGrid.Children.Add(btnMinimize);
                mainStack.Children.Add(headerGrid);

                topRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12, GridUnitType.Pixel) });
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                StackPanel sec1Stack = new StackPanel();
                sec1Stack.Children.Add(CreateKvRow("Scalper Trigger:", "EMA(3) x EMA(9)", "#FFFFFF"));
                sec1Stack.Children.Add(CreateKvRow("Trend Filter:", BSC_UsarEmaFiltro ? ("EMA(" + BSC_EmaFiltro + ") ACTIVE") : "OFF", BSC_UsarEmaFiltro ? "#10B981" : "#F59E0B"));
                sec1Stack.Children.Add(CreateKvRow("VWAP Filter:", BSC_UsarVwap ? "ACTIVE" : "OFF", BSC_UsarVwap ? "#10B981" : "#F59E0B"));
                sec1Stack.Children.Add(CreateKvRow("Contracts:", BSC_Contratos.ToString(), "#FFFFFF"));
                Border sec1 = CreateSectionBox("1. SCALPING INDICATORS", "#38BDF8", sec1Stack);
                Grid.SetColumn(sec1, 0);

                StackPanel sec2Stack = new StackPanel();
                sec2Stack.Children.Add(CreateKvRow("Stop Loss:", BSC_SL.ToString() + " ticks", "#FFFFFF"));
                sec2Stack.Children.Add(CreateKvRow("Take Profit:", BSC_TP.ToString() + " ticks", "#FFFFFF"));
                sec2Stack.Children.Add(CreateKvRow("Breakeven:", "+" + BSC_BETick.ToString() + " ticks", "#FFFFFF"));
                Border sec2 = CreateSectionBox("2. OCO ORDER TARGETS", "#FB923C", sec2Stack);
                Grid.SetColumn(sec2, 2);

                topRow.Children.Add(sec1); topRow.Children.Add(sec2);
                mainStack.Children.Add(topRow);

                midRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                midRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                midRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12, GridUnitType.Pixel) });
                midRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                StackPanel sec3Stack = new StackPanel();
                sec3Stack.Children.Add(CreateKvRow("Session Morning:", "09:35 - 11:45", "#FFFFFF"));
                sec3Stack.Children.Add(CreateKvRow("Session Afternoon:", "14:00 - 15:45", "#FFFFFF"));
                sec3Stack.Children.Add(CreateKvRow("Lunch Pause:", "11:45 - 14:00", "#F59E0B"));
                Border sec3 = CreateSectionBox("3. LIQUIDITY WINDOWS", "#34D399", sec3Stack);
                Grid.SetColumn(sec3, 0);

                StackPanel sec4Stack = new StackPanel();
                sec4Stack.Children.Add(CreateKvRow("Max Trades/Day:", BSC_MaxTrades > 0 ? BSC_MaxTrades.ToString() : "Unlimited", "#FFFFFF"));
                sec4Stack.Children.Add(CreateKvRow("Drawdown Guard:", BSC_UsarEscudoFondeo ? "ACTIVE ($" + BSC_MaxRetrocesoFlotante.ToString("N0") + ")" : "OFF", BSC_UsarEscudoFondeo ? "#10B981" : "#EF4444"));
                sec4Stack.Children.Add(CreateKvRow("Max Daily Loss:", "-$" + BSC_PerdMax.ToString("N0"), "#EF4444"));
                sec4Stack.Children.Add(CreateKvRow("Daily Target:", "+$" + BSC_GanObj.ToString("N0"), "#10B981"));
                Border sec4 = CreateSectionBox("4. RISK MANAGEMENT", "#A78BFA", sec4Stack);
                Grid.SetColumn(sec4, 2);

                midRow.Children.Add(sec3); midRow.Children.Add(sec4);
                mainStack.Children.Add(midRow);

                // Progress Bar
                StackPanel progressStack = new StackPanel();
                Grid progressHeaderGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                progressHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                progressHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                TextBlock progressTitle = new TextBlock { Text = "🎯 SCALPING TARGET PROGRESS", Foreground = HexColor("#F59E0B"), FontWeight = FontWeights.Bold, FontSize = 10.5 };
                Grid.SetColumn(progressTitle, 0);

                progressPctText = new TextBlock { Text = "$0.00 / +$" + BSC_GanObj.ToString("N0"), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 10.5 };
                Grid.SetColumn(progressPctText, 1);

                progressHeaderGrid.Children.Add(progressTitle); progressHeaderGrid.Children.Add(progressPctText);
                progressStack.Children.Add(progressHeaderGrid);

                Border progressBarTrack = new Border { Height = 14, Background = HexColor("#020710", 220), BorderBrush = HexColor("#1E293B"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 2, 0, 2) };
                progressBarFill = new Border { Height = 12, Width = 0, Background = HexColor("#10B981"), CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left };

                progressBarTrack.Child = progressBarFill;
                progressStack.Children.Add(progressBarTrack);
                progressBox = CreateSectionBox("PROGRESS TARGET", "#F59E0B", progressStack);
                mainStack.Children.Add(progressBox);

                // Live PnL Bar
                pnlBar = new Border { Background = HexColor("#030D1A", 200), BorderBrush = HexColor("#1E293B", 220), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 10) };
                Grid pnlGrid = new Grid();
                pnlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                pnlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                pnlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                realizedPnlText = new TextBlock { Text = "Realized: +$0.00", Foreground = HexColor("#10B981"), FontWeight = FontWeights.Bold, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
                Grid.SetColumn(realizedPnlText, 0);

                openPnlText = new TextBlock { Text = "Open PnL: +$0.00", Foreground = HexColor("#10B981"), FontWeight = FontWeights.Bold, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
                Grid.SetColumn(openPnlText, 1);

                tradesTodayText = new TextBlock { Text = "Trades Today: 0", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
                Grid.SetColumn(tradesTodayText, 2);

                pnlGrid.Children.Add(realizedPnlText); pnlGrid.Children.Add(openPnlText); pnlGrid.Children.Add(tradesTodayText);
                pnlBar.Child = pnlGrid;
                mainStack.Children.Add(pnlBar);

                // Buttons
                btnGrid = new Grid();
                btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
                btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
                btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                btnFlatten = new Button { Content = "FLATTEN & CANCEL ALL", Background = HexColor("#EF4444", 220), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 11, Height = 34, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
                btnFlatten.Click += (s, e) => { BSC_EjecutarFlattenYCancelarTodo(); };
                Grid.SetColumn(btnFlatten, 0);

                btnPause = new Button { Content = "PAUSE SCALPER", Background = HexColor("#F97316", 220), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 11, Height = 34, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
                btnPause.Click += (s, e) => {
                    isPaused = !isPaused;
                    if (statusText != null) {
                        statusText.Text = isPaused ? "STATE: PAUSED" : "STATE: ACTIVE / LIVE";
                        statusText.Foreground = isPaused ? HexColor("#F97316") : HexColor("#10B981");
                    }
                };
                Grid.SetColumn(btnPause, 2);

                btnReset = new Button { Content = "RESET PnL", Background = HexColor("#10B981", 220), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 11, Height = 34, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
                btnReset.Click += (s, e) => {
                    bsc_pnl = 0; bsc_ops = 0; bsc_lock = false; bsc_highWater = 0;
                    UpdateWpfHudMetrics();
                };
                Grid.SetColumn(btnReset, 4);

                btnGrid.Children.Add(btnFlatten); btnGrid.Children.Add(btnPause); btnGrid.Children.Add(btnReset);
                mainStack.Children.Add(btnGrid);

                hudBorder.Child = mainStack;
                EnsureHudAttachedToChart();
            }
            catch {}
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
                        if (!g.Children.Contains(hudBorder)) g.Children.Add(hudBorder);
                        return;
                    }
                    else if (current is Panel)
                    {
                        Panel p = (Panel)current;
                        if (!p.Children.Contains(hudBorder)) p.Children.Add(hudBorder);
                        return;
                    }
                    DependencyObject parent = null;
                    if (current is FrameworkElement) parent = ((FrameworkElement)current).Parent;
                    if (parent == null) parent = VisualTreeHelper.GetParent(current);
                    current = parent;
                }
            }
            catch {}
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
                            if (g.Children.Contains(hudBorder)) g.Children.Remove(hudBorder);
                        }
                        else if (current is Panel)
                        {
                            Panel p = (Panel)current;
                            if (p.Children.Contains(hudBorder)) p.Children.Remove(hudBorder);
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
                    if (realizedPnlText != null) realizedPnlText.Text = "Realized: " + (bsc_pnl >= 0 ? "+" : "") + "$" + bsc_pnl.ToString("N2");
                    if (tradesTodayText != null) tradesTodayText.Text = "Trades Today: " + bsc_ops;
                    if (openPnlText != null && Position != null)
                    {
                        double openPnl = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
                        openPnlText.Text = "Open PnL: " + (openPnl >= 0 ? "+" : "") + "$" + openPnl.ToString("N2");
                    }

                    if (progressPctText != null && progressBarFill != null)
                    {
                        double targetVal = Math.Max(1, BSC_GanObj);
                        double pct = Math.Max(0, Math.Min(100, (bsc_pnl / targetVal) * 100));

                        if (bsc_pnl >= BSC_GanObj)
                        {
                            progressPctText.Text = "🎯 TARGET REACHED! (+$" + bsc_pnl.ToString("N2") + ")";
                            progressPctText.Foreground = HexColor("#10B981");
                            progressBarFill.Width = 410;
                            progressBarFill.Background = HexColor("#10B981");
                        }
                        else if (bsc_pnl < 0)
                        {
                            double lossPct = Math.Min(100, (Math.Abs(bsc_pnl) / Math.Max(1, BSC_PerdMax)) * 100);
                            progressPctText.Text = "⚠️ Loss: -$" + Math.Abs(bsc_pnl).ToString("N2") + " (" + lossPct.ToString("F1") + "% Max)";
                            progressPctText.Foreground = HexColor("#EF4444");
                            progressBarFill.Width = Math.Max(10, (lossPct / 100) * 410);
                            progressBarFill.Background = HexColor("#EF4444");
                        }
                        else
                        {
                            progressPctText.Text = "+$" + bsc_pnl.ToString("N2") + " / +$" + targetVal.ToString("N0") + " (" + pct.ToString("F1") + "%)";
                            progressPctText.Foreground = Brushes.White;
                            progressBarFill.Width = Math.Max(0, (pct / 100) * 410);
                            progressBarFill.Background = HexColor("#F59E0B");
                        }
                    }
                }), DispatcherPriority.Background);
            }
        }

        private void BSC_EjecutarFlattenYCancelarTodo()
        {
            try
            {
                bsc_lock = true;
                isPaused = true;

                if (statusText != null)
                {
                    statusText.Text = "STATE: FLATTENED & PAUSED";
                    statusText.Foreground = HexColor("#EF4444");
                }

                if (Account != null && Account.Orders != null)
                {
                    List<Order> workingOrders = new List<Order>();
                    foreach (Order o in Account.Orders)
                    {
                        if (o != null && (o.OrderState == OrderState.Working || o.OrderState == OrderState.Submitted || o.OrderState == OrderState.Accepted))
                            workingOrders.Add(o);
                    }
                    if (workingOrders.Count > 0) Account.Cancel(workingOrders.ToArray());
                }

                if (Position != null && Position.MarketPosition != MarketPosition.Flat)
                {
                    if (Position.MarketPosition == MarketPosition.Long) ExitLong("BSC_L");
                    else if (Position.MarketPosition == MarketPosition.Short) ExitShort("BSC_S");
                    if (Account != null && Instrument != null) Account.Flatten(new[] { Instrument });
                }
            }
            catch (Exception ex)
            {
                Print("[BSC FLATTEN ERROR] " + ex.Message);
            }
        }
        #endregion
    }
}
