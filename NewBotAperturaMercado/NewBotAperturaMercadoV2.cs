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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public enum RocketRecoveryModeEnum
    {
        Disabled,
        SameDirection,
        ReverseDirection,
        AutoSmartFilter
    }

    public class NewBotAperturaMercadoV2 : Strategy
    {
        #region Private Fields
        private EMA emaFast;
        private EMA emaMid;

        // Métricas Diarias
        private double dailyCumProfit = 0;
        private int tradesTodayCount = 0;
        private int winsTodayCount = 0;
        private int lossesTodayCount = 0;
        private DateTime currentTradeDate = DateTime.MinValue;
        private bool dailyPnLocked = false;
        private int barsInPosition = 0;
        private int currentLockStage = 0;
        private bool isEntryTriggeredToday = false;

        // Estrategia Cohete de Recuperación Inmediata State
        private bool rocketExecutedToday = false;
        private bool isRocketTradeActive = false;

        // Guardián de Rango Pre-Apertura
        private double preEntryHigh = double.MinValue;
        private double preEntryLow = double.MaxValue;
        private double resolvedRangePoints = 0;
        private string resolvedDirection = "NONE";
        private bool rangeCalculatedToday = false;

        // Timed Cut & Adaptive Management State
        private DateTime entryTimestamp = DateTime.MinValue;
        private bool timedCutEvaluatedToday = false;

        // Estado del Bot
        private bool isPaused = false;
        private bool isArmed = true;

        // Elementos de la Ventana Flotante WPF (Independiente)
        private Window controlWindow;
        private TextBlock txtStatusPill;
        private TextBlock txtBid;
        private TextBlock txtAsk;
        private TextBlock txtMarketTime;
        private TextBlock txtRealizedPnl;
        private TextBlock txtOpenPnl;
        private TextBlock txtPreRangeInfo;
        private ComboBox comboProfile;
        private TextBox txtLogOutput;
        private Button btnArmDisarm;
        private Button btnFlatten;

        // UI Controls Dinámicos para Perfiles y Edición CUSTOM
        private TextBox txtContractsUI;
        private TextBlock txtStopLossUI;
        private TextBlock txtProfitTargetUI;
        private TextBox txtDailyMaxLossUI;
        private TextBox txtDailyProfitTargetUI;
        private TextBlock txtStage1UI;
        private TextBlock txtStage2UI;
        private TextBlock txtStage3UI;
        private TextBlock txtStage4UI;

        // UI Controls para Adaptive Management
        private CheckBox chkTimedCutArmedUI;
        private TextBox txtSlowRRRangeMaxUI;
        private TextBox txtSlowRRMinHitSecUI;
        private TextBox txtTimedRangeMaxUI;
        private TextBox txtEvaluationSecUI;
        private TextBox txtLossCutUI;
        private TextBlock txtAdaptiveStatusUI;

        // UI Controls para Cohete Recuperación
        private CheckBox chkUseRocketRecoveryUI;
        private ComboBox comboRocketModeUI;
        private TextBox txtRocketStopLossUI;
        private TextBox txtRocketProfitTargetUI;
        private TextBox txtRocketWickFilterUI;
        private TextBlock txtRocketStatusUI;

        private bool isUpdatingUI = false;
        #endregion

        #region 1. Perfil & Posición
        [NinjaScriptProperty]
        [Display(Name = "Perfil de Cuenta", Order = 1, GroupName = "1. Perfil & Posición")]
        public AccountProfileEnum SelectedProfile { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Contratos / Lotes", Order = 2, GroupName = "1. Perfil & Posición")]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Regla de Dirección", Order = 3, GroupName = "1. Perfil & Posición")]
        public TradeDirectionMode DirectionRule { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Deslizamiento (Slippage Ticks)", Order = 4, GroupName = "1. Perfil & Posición")]
        public int UserSlippageTicks { get; set; }
        #endregion

        #region 2. Horario & Disparo Programado
        [NinjaScriptProperty]
        [Display(Name = "Hora Disparo NY (HHMMSS)", Description = "Hora exacta de disparo (ej: 92958 para 09:29:58 NY)", Order = 1, GroupName = "2. Horario & Timed Entry")]
        public int TimedEntryTimeNY { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Ventana Tolerancia Disparo (Segs)", Order = 2, GroupName = "2. Horario & Timed Entry")]
        public int EntryWindowSeconds { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Máx Retraso Entrada Tardía (Segs)", Order = 3, GroupName = "2. Horario & Timed Entry")]
        public int MaxLateEntrySeconds { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Hora Cierre Forzoso NY (HHMMSS)", Order = 4, GroupName = "2. Horario & Timed Entry")]
        public int ForceFlatTimeNY { get; set; }
        #endregion

        #region 3. Guardián de Rango Pre-Apertura
        [NinjaScriptProperty]
        [Display(Name = "Inicio Rango Pre-Apertura (HHMMSS)", Order = 1, GroupName = "3. Rango Pre-Apertura")]
        public int PreRangeStartTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Fin Rango Pre-Apertura (HHMMSS)", Order = 2, GroupName = "3. Rango Pre-Apertura")]
        public int PreRangeEndTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Umbral de Rango (Puntos)", Order = 3, GroupName = "3. Rango Pre-Apertura")]
        public double RangeThresholdPoints { get; set; }
        #endregion

        #region 4. Gestión de Riesgo & Profit Lock (4 Stages)
        [NinjaScriptProperty]
        [Display(Name = "Stop Loss (Ticks)", Order = 1, GroupName = "4. Control de Riesgo & Profit Lock")]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Profit Target (Ticks)", Order = 2, GroupName = "4. Control de Riesgo & Profit Lock")]
        public int ProfitTargetTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Pérdida Máxima Diaria ($)", Order = 3, GroupName = "4. Control de Riesgo & Profit Lock")]
        public double DailyMaxLossDollars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Objetivo de Ganancia Diario ($)", Order = 4, GroupName = "4. Control de Riesgo & Profit Lock")]
        public double DailyProfitTargetDollars { get; set; }

        // Stage 1
        [NinjaScriptProperty]
        [Display(Name = "Profit Lock Stage 1 Trigger ($)", Order = 5, GroupName = "4. Control de Riesgo & Profit Lock")]
        public double Stage1Trigger { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Profit Lock Stage 1 Secure ($)", Order = 6, GroupName = "4. Control de Riesgo & Profit Lock")]
        public double Stage1Secure { get; set; }

        // Stage 2
        [NinjaScriptProperty]
        [Display(Name = "Profit Lock Stage 2 Trigger ($)", Order = 7, GroupName = "4. Control de Riesgo & Profit Lock")]
        public double Stage2Trigger { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Profit Lock Stage 2 Secure ($)", Order = 8, GroupName = "4. Control de Riesgo & Profit Lock")]
        public double Stage2Secure { get; set; }

        // Stage 3
        [NinjaScriptProperty]
        [Display(Name = "Profit Lock Stage 3 Trigger ($)", Order = 9, GroupName = "4. Control de Riesgo & Profit Lock")]
        public double Stage3Trigger { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Profit Lock Stage 3 Secure ($)", Order = 10, GroupName = "4. Control de Riesgo & Profit Lock")]
        public double Stage3Secure { get; set; }

        // Stage 4
        [NinjaScriptProperty]
        [Display(Name = "Profit Lock Stage 4 Trigger ($)", Order = 11, GroupName = "4. Control de Riesgo & Profit Lock")]
        public double Stage4Trigger { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Profit Lock Stage 4 Secure ($)", Order = 12, GroupName = "4. Control de Riesgo & Profit Lock")]
        public double Stage4Secure { get; set; }
        #endregion

        #region 5. Adaptive Management & Timed Cut
        [NinjaScriptProperty]
        [Display(Name = "Slow RR Range Max (Pts)", Order = 1, GroupName = "5. Adaptive Management")]
        public double SlowRRRangeMax { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Slow RR Min Hit Secs", Order = 2, GroupName = "5. Adaptive Management")]
        public int SlowRRMinHitSec { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Timed Cut Armed", Order = 3, GroupName = "5. Adaptive Management")]
        public bool TimedCutArmed { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Timed Range Max (Pts)", Order = 4, GroupName = "5. Adaptive Management")]
        public double TimedRangeMax { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Evaluation Secs", Order = 5, GroupName = "5. Adaptive Management")]
        public double EvaluationSec { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Loss Cut ($)", Order = 6, GroupName = "5. Adaptive Management")]
        public double LossCutDollars { get; set; }
        #endregion

        #region 6. Estrategia Cohete de Recuperación Inmediata
        [NinjaScriptProperty]
        [Display(Name = "Activar Cohete Recuperación", Order = 1, GroupName = "6. Cohete Recuperación")]
        public bool UseRocketRecovery { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Modo Disparo Cohete", Order = 2, GroupName = "6. Cohete Recuperación")]
        public RocketRecoveryModeEnum RocketMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cohete Stop Loss (Ticks)", Order = 3, GroupName = "6. Cohete Recuperación")]
        public int RocketStopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cohete Profit Target (Ticks)", Order = 4, GroupName = "6. Cohete Recuperación")]
        public int RocketProfitTargetTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Umbral Mecha VSA (%)", Order = 5, GroupName = "6. Cohete Recuperación")]
        public double RocketWickFilterPercent { get; set; }
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Bot de Apertura de Mercado 30s V2 con Adaptive Management y WPF Control Panel.";
                Name = "NewBotAperturaMercadoV2";
                Calculate = Calculate.OnPriceChange;
                IsInstantiatedOnEachOptimizationIteration = false;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                BarsRequiredToTrade = 20;

                SelectedProfile = AccountProfileEnum.Profile50K;
                Contracts = 15;
                DirectionRule = TradeDirectionMode.Auto;
                UserSlippageTicks = 1;

                TimedEntryTimeNY = 92958;
                EntryWindowSeconds = 3;
                MaxLateEntrySeconds = 5;
                ForceFlatTimeNY = 155000;

                PreRangeStartTime = 90000;
                PreRangeEndTime = 92859;
                RangeThresholdPoints = 50.0;
                UseTrendFilter = true;

                StopLossTicks = 133;
                ProfitTargetTicks = 334;
                DailyMaxLossDollars = 1000;
                DailyProfitTargetDollars = 2500;

                Stage1Trigger = 600;  Stage1Secure = 520;
                Stage2Trigger = 1000; Stage2Secure = 820;
                Stage3Trigger = 1150; Stage3Secure = 1050;
                Stage4Trigger = 1800; Stage4Secure = 1600;

                SlowRRRangeMax = 42.5;
                SlowRRMinHitSec = 3;
                TimedCutArmed = true;
                TimedRangeMax = 37.5;
                EvaluationSec = 7.5;
                LossCutDollars = 300.0;

                UseRocketRecovery = true;
                RocketMode = RocketRecoveryModeEnum.AutoSmartFilter;
                RocketStopLossTicks = 40;
                RocketProfitTargetTicks = 80;
                RocketWickFilterPercent = 40.0;
            }
            else if (State == State.Configure)
            {
                AplicarPerfilCuenta(SelectedProfile);
            }
            else if (State == State.DataLoaded)
            {
                emaFast = EMA(9);
                emaMid = EMA(21);

                if (ChartControl != null && ChartControl.Dispatcher != null)
                {
                    ChartControl.Dispatcher.BeginInvoke(new Action(() => {
                        InicializarVentanaFlotanteWPF();
                    }), DispatcherPriority.Normal);
                }
            }
            else if (State == State.Realtime)
            {
                AppendLog("[INFO V2] V2 en tiempo real iniciado. Esperando ventana de pre-apertura 09:00:00 NY.");
            }
            else if (State == State.Terminated)
            {
                CerrarVentanaFlotanteWPF();
            }
        }

        #region Helper Método de Tiempo
        private TimeSpan ParseTimeInt(int timeInt)
        {
            int hours = timeInt / 10000;
            int minutes = (timeInt % 10000) / 100;
            int seconds = timeInt % 100;
            return new TimeSpan(hours, minutes, seconds);
        }
        #endregion

        #region Aplicación de Perfil de Cuenta & Actualización UI Dinámica
        public void AplicarPerfilCuenta(AccountProfileEnum profile)
        {
            SelectedProfile = profile;
            switch (profile)
            {
                case AccountProfileEnum.Profile25K:
                    Contracts = 7;
                    DailyMaxLossDollars = 500;
                    DailyProfitTargetDollars = 1250;
                    Stage1Trigger = 300;  Stage1Secure = 260;
                    Stage2Trigger = 500;  Stage2Secure = 410;
                    Stage3Trigger = 575;  Stage3Secure = 525;
                    Stage4Trigger = 900;  Stage4Secure = 800;
                    LossCutDollars = 150;
                    break;

                case AccountProfileEnum.Profile50K:
                    Contracts = 15;
                    DailyMaxLossDollars = 1000;
                    DailyProfitTargetDollars = 2500;
                    Stage1Trigger = 600;  Stage1Secure = 520;
                    Stage2Trigger = 1000; Stage2Secure = 820;
                    Stage3Trigger = 1150; Stage3Secure = 1050;
                    Stage4Trigger = 1800; Stage4Secure = 1600;
                    LossCutDollars = 300;
                    break;

                case AccountProfileEnum.Profile100K:
                    Contracts = 22;
                    DailyMaxLossDollars = 1500;
                    DailyProfitTargetDollars = 3750;
                    Stage1Trigger = 900;  Stage1Secure = 780;
                    Stage2Trigger = 1500; Stage2Secure = 1230;
                    Stage3Trigger = 1725; Stage3Secure = 1575;
                    Stage4Trigger = 2700; Stage4Secure = 2400;
                    LossCutDollars = 450;
                    break;

                case AccountProfileEnum.Profile150K:
                    Contracts = 30;
                    DailyMaxLossDollars = 2000;
                    DailyProfitTargetDollars = 5000;
                    Stage1Trigger = 1200; Stage1Secure = 1040;
                    Stage2Trigger = 2000; Stage2Secure = 1640;
                    Stage3Trigger = 2300; Stage3Secure = 2100;
                    Stage4Trigger = 3600; Stage4Secure = 3200;
                    LossCutDollars = 600;
                    break;

                case AccountProfileEnum.Custom:
                    RecalcularValoresProporcionales(Contracts);
                    break;
            }

            ActualizarUIValoresPerfil();
            AppendLog($"[EXEC V2] Perfil aplicado: {profile} | Contratos: {Contracts} | Riesgo: ${DailyMaxLossDollars} | Target: ${DailyProfitTargetDollars} | Loss Cut: ${LossCutDollars}");
        }

        public void RecalcularValoresProporcionales(int numContracts)
        {
            Contracts = Math.Max(1, numContracts);
            double factor = Contracts / 15.0;

            DailyMaxLossDollars = Math.Round(1000.0 * factor);
            DailyProfitTargetDollars = Math.Round(2500.0 * factor);

            Stage1Trigger = Math.Round(600.0 * factor);
            Stage1Secure  = Math.Round(520.0 * factor);

            Stage2Trigger = Math.Round(1000.0 * factor);
            Stage2Secure  = Math.Round(820.0 * factor);

            Stage3Trigger = Math.Round(1150.0 * factor);
            Stage3Secure  = Math.Round(1050.0 * factor);

            Stage4Trigger = Math.Round(1800.0 * factor);
            Stage4Secure  = Math.Round(1600.0 * factor);

            LossCutDollars = Math.Round(300.0 * factor);
        }

        private void ActualizarUIValoresPerfil()
        {
            if (controlWindow == null || controlWindow.Dispatcher == null) return;

            controlWindow.Dispatcher.BeginInvoke(new Action(() => {
                isUpdatingUI = true;
                try
                {
                    bool isCustom = (SelectedProfile == AccountProfileEnum.Custom);

                    if (txtContractsUI != null)
                    {
                        txtContractsUI.Text = Contracts.ToString();
                        txtContractsUI.IsReadOnly = !isCustom;
                        txtContractsUI.Background = isCustom ? HexColor("#0F172A") : HexColor("#020710", 220);
                        txtContractsUI.BorderBrush = isCustom ? HexColor("#38BDF8") : HexColor("#1E293B");
                    }

                    if (txtDailyMaxLossUI != null)
                    {
                        txtDailyMaxLossUI.Text = "-$" + DailyMaxLossDollars.ToString("N0");
                        txtDailyMaxLossUI.IsReadOnly = !isCustom;
                        txtDailyMaxLossUI.Background = isCustom ? HexColor("#0F172A") : HexColor("#020710", 220);
                    }

                    if (txtDailyProfitTargetUI != null)
                    {
                        txtDailyProfitTargetUI.Text = "+$" + DailyProfitTargetDollars.ToString("N0");
                        txtDailyProfitTargetUI.IsReadOnly = !isCustom;
                        txtDailyProfitTargetUI.Background = isCustom ? HexColor("#0F172A") : HexColor("#020710", 220);
                    }

                    if (txtStage1UI != null) txtStage1UI.Text = $"Trigger ${Stage1Trigger:N0} → Secure ${Stage1Secure:N0}";
                    if (txtStage2UI != null) txtStage2UI.Text = $"Trigger ${Stage2Trigger:N0} → Secure ${Stage2Secure:N0}";
                    if (txtStage3UI != null) txtStage3UI.Text = $"Trigger ${Stage3Trigger:N0} → Secure ${Stage3Secure:N0}";
                    if (txtStage4UI != null) txtStage4UI.Text = $"Trigger ${Stage4Trigger:N0} → Secure ${Stage4Secure:N0}";

                    if (chkTimedCutArmedUI != null) chkTimedCutArmedUI.IsChecked = TimedCutArmed;
                    if (txtSlowRRRangeMaxUI != null) txtSlowRRRangeMaxUI.Text = SlowRRRangeMax.ToString("F1");
                    if (txtSlowRRMinHitSecUI != null) txtSlowRRMinHitSecUI.Text = SlowRRMinHitSec.ToString();
                    if (txtTimedRangeMaxUI != null) txtTimedRangeMaxUI.Text = TimedRangeMax.ToString("F1");
                    if (txtEvaluationSecUI != null) txtEvaluationSecUI.Text = EvaluationSec.ToString("F1");
                    if (txtLossCutUI != null) txtLossCutUI.Text = LossCutDollars.ToString("N0");

                    if (chkUseRocketRecoveryUI != null) chkUseRocketRecoveryUI.IsChecked = UseRocketRecovery;
                    if (comboRocketModeUI != null) comboRocketModeUI.SelectedIndex = (int)RocketMode;
                    if (txtRocketStopLossUI != null) txtRocketStopLossUI.Text = RocketStopLossTicks.ToString();
                    if (txtRocketProfitTargetUI != null) txtRocketProfitTargetUI.Text = RocketProfitTargetTicks.ToString();
                    if (txtRocketWickFilterUI != null) txtRocketWickFilterUI.Text = RocketWickFilterPercent.ToString("F0");
                }
                finally
                {
                    isUpdatingUI = false;
                }
            }), DispatcherPriority.Background);
        }
        #endregion

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade || !isArmed || isPaused)
                return;

            DateTime timeNow = Time[0];
            TimeSpan barTimeSpan = timeNow.TimeOfDay;

            if (timeNow.Date != currentTradeDate)
            {
                currentTradeDate = timeNow.Date;
                dailyCumProfit = 0;
                tradesTodayCount = 0;
                winsTodayCount = 0;
                lossesTodayCount = 0;
                dailyPnLocked = false;
                isEntryTriggeredToday = false;
                preEntryHigh = double.MinValue;
                preEntryLow = double.MaxValue;
                rangeCalculatedToday = false;
                resolvedDirection = "NONE";
                currentLockStage = 0;
                timedCutEvaluatedToday = false;
                rocketExecutedToday = false;
                isRocketTradeActive = false;
                entryTimestamp = DateTime.MinValue;
                AppendLog($"[INFO V2] Nuevo día de trading iniciado: {currentTradeDate:yyyy-MM-dd}");
            }

            TimeSpan preStart = ParseTimeInt(PreRangeStartTime);
            TimeSpan preEnd = ParseTimeInt(PreRangeEndTime);

            if (barTimeSpan >= preStart && barTimeSpan <= preEnd)
            {
                preEntryHigh = Math.Max(preEntryHigh, High[0]);
                preEntryLow = Math.Min(preEntryLow, Low[0]);
            }
            else if (barTimeSpan > preEnd && !rangeCalculatedToday && preEntryHigh > double.MinValue && preEntryLow < double.MaxValue)
            {
                rangeCalculatedToday = true;
                resolvedRangePoints = preEntryHigh - preEntryLow;

                if (DirectionRule == TradeDirectionMode.Auto)
                {
                    double midPoint = (preEntryHigh + preEntryLow) / 2;

                    // Strict Trend Alignment Filter: EMA 9 must be aligned with EMA 21
                    bool strictBullish = (emaFast[0] > emaMid[0] && Close[0] > emaFast[0]);
                    bool strictBearish = (emaFast[0] < emaMid[0] && Close[0] < emaFast[0]);

                    if (Close[0] >= midPoint && strictBullish)
                    {
                        resolvedDirection = "LONG";
                    }
                    else if (Close[0] < midPoint && strictBearish)
                    {
                        resolvedDirection = "SHORT";
                    }
                    else
                    {
                        resolvedDirection = "NONE";
                    }
                }
                else if (DirectionRule == TradeDirectionMode.LongOnly) resolvedDirection = "LONG";
                else if (DirectionRule == TradeDirectionMode.ShortOnly) resolvedDirection = "SHORT";

                AppendLog($"[RANGE GUARD V2] Rango resuelto: {resolvedRangePoints:F2} pts (Umbral: {RangeThresholdPoints} pts) -> Dirección AUTO: {resolvedDirection}");
                ActualizarPreRangoUI();
            }

            if (State == State.Realtime) ActualizarTelemetriaUI();

            TimeSpan forceFlatSpan = ParseTimeInt(ForceFlatTimeNY);
            if (barTimeSpan >= forceFlatSpan && Position.MarketPosition != MarketPosition.Flat)
            {
                EjecutarFlattenYCancelarTodo();
                AppendLog("[RISK V2] Cierre forzoso ejecutado por horario 15:50:00 NY.");
                return;
            }

            if (dailyPnLocked)
            {
                if (Position.MarketPosition != MarketPosition.Flat)
                    EjecutarFlattenYCancelarTodo();
                return;
            }

            TimeSpan entryStartSpan = ParseTimeInt(TimedEntryTimeNY);
            TimeSpan entryEndSpan = entryStartSpan.Add(TimeSpan.FromSeconds(EntryWindowSeconds + 5));

            if (!isEntryTriggeredToday && barTimeSpan >= entryStartSpan && barTimeSpan <= entryEndSpan)
            {
                if (Position.MarketPosition == MarketPosition.Flat)
                {
                    isEntryTriggeredToday = true;
                    entryTimestamp = Time[0];
                    timedCutEvaluatedToday = false;

                    string dir = (DirectionRule == TradeDirectionMode.Auto) ? resolvedDirection : DirectionRule.ToString().Replace("Only", "").ToUpper();

                    if (dir == "NONE")
                    {
                        AppendLog($"[TREND FILTER V2] 🛑 Conflicto entre Rango Pre-Apertura y EMAs (9/21). Entrada cancelada hoy para evitar perdidas en mercado sucio.");
                        return;
                    }

                    if (dir == "LONG" || dir == "BOTH")
                    {
                        SetStopLoss(CalculationMode.Ticks, StopLossTicks);
                        SetProfitTarget(CalculationMode.Ticks, ProfitTargetTicks);
                        EnterLong(Contracts, "BotApertura_Long");
                        tradesTodayCount++;
                        barsInPosition = 0;
                        AppendLog($"[EXEC V2] Entrada TIMED LONG ejecutada ({Contracts} contratos) @ {Close[0]}");
                    }
                    else if (dir == "SHORT")
                    {
                        SetStopLoss(CalculationMode.Ticks, StopLossTicks);
                        SetProfitTarget(CalculationMode.Ticks, ProfitTargetTicks);
                        EnterShort(Contracts, "BotApertura_Short");
                        tradesTodayCount++;
                        barsInPosition = 0;
                        AppendLog($"[EXEC V2] Entrada TIMED SHORT ejecutada ({Contracts} contratos) @ {Close[0]}");
                    }
                }
            }

            // ---------------- ADAPTIVE MANAGEMENT & TIMED CUT LOGIC ----------------
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                barsInPosition++;
                double openProfitDollars = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
                double elapsedSeconds = (Time[0] - entryTimestamp).TotalSeconds;

                // Evaluación Timed Cut a los N segundos (default 7.5s)
                if (TimedCutArmed && !timedCutEvaluatedToday && (elapsedSeconds >= EvaluationSec || barsInPosition >= 1))
                {
                    timedCutEvaluatedToday = true;
                    AppendLog($"[TIMED CUT EVALUATION V2] Evaluando operación a los {elapsedSeconds:F1}s | PnL Abierto: ${openProfitDollars:N2} | Rango: {resolvedRangePoints:F1} pts (Máx: {TimedRangeMax} pts)");

                    if (resolvedRangePoints <= TimedRangeMax && openProfitDollars <= -Math.Abs(LossCutDollars))
                    {
                        EjecutarFlattenYCancelarTodo();
                        ActualizarAdaptiveStatusUI($"TIMED CUT EJECUTADO (-${Math.Abs(openProfitDollars):N0} a los {elapsedSeconds:F1}s)");
                        AppendLog($"[TIMED CUT TRIGGERED V2] ⚠️ Cierre de emergencia a los {elapsedSeconds:F1}s. PnL: ${openProfitDollars:N2} (Límite: -${LossCutDollars}). Evitado SL completo.");
                        return;
                    }
                    else
                    {
                        ActualizarAdaptiveStatusUI($"TIMED CUT PASADO (PnL ${openProfitDollars:N0} OK a los {elapsedSeconds:F1}s)");
                    }
                }

                // Profit Lock (4 Stages) - Aplica a la Operación Inicial y a la Operación Cohete
                string lockTag = isRocketTradeActive ? "[🚀 ROCKET PROFIT LOCK V2]" : "[PROFIT LOCK V2]";

                if (openProfitDollars >= Stage4Trigger && currentLockStage < 4)
                {
                    currentLockStage = 4;
                    double secureTicks = Stage4Secure / (Contracts * Instrument.MasterInstrument.PointValue * TickSize);
                    SetStopLoss(CalculationMode.Ticks, -Math.Abs(secureTicks));
                    double netEstimated = dailyCumProfit + Stage4Secure;
                    AppendLog($"{lockTag} Stage 4 alcanzado (${openProfitDollars:N0})! Asegurando +${Stage4Secure:N0}. PnL Neto Día Estimado: ${netEstimated:N2}");
                    if (isRocketTradeActive) ActualizarRocketStatusUI($"🚀 STAGE 4 LOCK (+$ {Stage4Secure:N0} Asegurados)");
                }
                else if (openProfitDollars >= Stage3Trigger && currentLockStage < 3)
                {
                    currentLockStage = 3;
                    double secureTicks = Stage3Secure / (Contracts * Instrument.MasterInstrument.PointValue * TickSize);
                    SetStopLoss(CalculationMode.Ticks, -Math.Abs(secureTicks));
                    double netEstimated = dailyCumProfit + Stage3Secure;
                    AppendLog($"{lockTag} Stage 3 alcanzado (${openProfitDollars:N0})! Asegurando +${Stage3Secure:N0}. PnL Neto Día Estimado: ${netEstimated:N2}");
                    if (isRocketTradeActive) ActualizarRocketStatusUI($"🚀 STAGE 3 LOCK (+$ {Stage3Secure:N0} Asegurados)");
                }
                else if (openProfitDollars >= Stage2Trigger && currentLockStage < 2)
                {
                    currentLockStage = 2;
                    double secureTicks = Stage2Secure / (Contracts * Instrument.MasterInstrument.PointValue * TickSize);
                    SetStopLoss(CalculationMode.Ticks, -Math.Abs(secureTicks));
                    double netEstimated = dailyCumProfit + Stage2Secure;
                    AppendLog($"{lockTag} Stage 2 alcanzado (${openProfitDollars:N0})! Asegurando +${Stage2Secure:N0}. PnL Neto Día Estimado: ${netEstimated:N2}");
                    if (isRocketTradeActive) ActualizarRocketStatusUI($"🚀 STAGE 2 LOCK (+$ {Stage2Secure:N0} Asegurados)");
                }
                else if (openProfitDollars >= Stage1Trigger && currentLockStage < 1)
                {
                    currentLockStage = 1;
                    double secureTicks = Stage1Secure / (Contracts * Instrument.MasterInstrument.PointValue * TickSize);
                    SetStopLoss(CalculationMode.Ticks, -Math.Abs(secureTicks));
                    double netEstimated = dailyCumProfit + Stage1Secure;
                    AppendLog($"{lockTag} Stage 1 alcanzado (${openProfitDollars:N0})! Asegurando +${Stage1Secure:N0} (Garantizando PnL Día Neto: ${netEstimated:N2})");
                    if (isRocketTradeActive) ActualizarRocketStatusUI($"🚀 STAGE 1 LOCK (+$ {Stage1Secure:N0} Asegurados)");
                }
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (SystemPerformance != null && SystemPerformance.AllTrades != null && SystemPerformance.AllTrades.Count > 0)
            {
                Trade lastTrade = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1];
                if (lastTrade != null && lastTrade.Exit != null && execution.ExecutionId == lastTrade.Exit.Execution.ExecutionId)
                {
                    double pnl = lastTrade.ProfitCurrency;
                    dailyCumProfit += pnl;

                    if (pnl >= 0) winsTodayCount++;
                    else lossesTodayCount++;

                    if (dailyCumProfit <= -Math.Abs(DailyMaxLossDollars) || dailyCumProfit >= DailyProfitTargetDollars)
                    {
                        dailyPnLocked = true;
                        AppendLog($"[RISK LOCK V2] Bloqueo diario activado. PnL acumulado: ${dailyCumProfit:N2}");
                    }

                    // ---------------- ESTRATEGIA COHETE: GATILLAZO INMEDIATO POST-STOP LOSS ----------------
                    if (pnl < 0 && UseRocketRecovery && RocketMode != RocketRecoveryModeEnum.Disabled && !rocketExecutedToday && !dailyPnLocked && Position.MarketPosition == MarketPosition.Flat)
                    {
                        rocketExecutedToday = true;
                        isRocketTradeActive = true;
                        currentLockStage = 0;

                        MarketPosition lastPos = lastTrade.Entry.MarketPosition;
                        string lastDirStr = (lastPos == MarketPosition.Long) ? "LONG" : "SHORT";
                        string rocketDir = "NONE";

                        // Métricas de la vela de 30s actual (VSA Mecha & EMAs)
                        double barRange = High[0] - Low[0];
                        double upperWick = High[0] - Math.Max(Open[0], Close[0]);
                        double lowerWick = Math.Min(Open[0], Close[0]) - Low[0];
                        double lowerWickPct = (barRange > 0) ? (lowerWick / barRange) * 100.0 : 0;
                        double upperWickPct = (barRange > 0) ? (upperWick / barRange) * 100.0 : 0;

                        bool emaBullish = (Close[0] > emaFast[0] && emaFast[0] > emaMid[0]);
                        bool emaBearish = (Close[0] < emaFast[0] && emaFast[0] < emaMid[0]);

                        if (RocketMode == RocketRecoveryModeEnum.SameDirection)
                        {
                            rocketDir = lastDirStr;
                        }
                        else if (RocketMode == RocketRecoveryModeEnum.ReverseDirection)
                        {
                            rocketDir = (lastDirStr == "LONG") ? "SHORT" : "LONG";
                        }
                        else if (RocketMode == RocketRecoveryModeEnum.AutoSmartFilter)
                        {
                            if (lastDirStr == "LONG")
                            {
                                // Estábamos comprados y nos saltó el SL hacia abajo.
                                // Si mecha inferior >= umbral VSA O EMAs alcistas -> Reabsorción -> Re-comprar Long
                                if (lowerWickPct >= RocketWickFilterPercent || emaBullish)
                                {
                                    rocketDir = "LONG";
                                }
                                else
                                {
                                    rocketDir = "SHORT"; // Caída limpia de cuerpo lleno -> Stop & Reverse Venta
                                }
                            }
                            else // lastDirStr == "SHORT"
                            {
                                // Estábamos vendidos y nos saltó el SL hacia arriba.
                                // Si mecha superior >= umbral VSA O EMAs bajistas -> Reabsorción -> Re-vender Short
                                if (upperWickPct >= RocketWickFilterPercent || emaBearish)
                                {
                                    rocketDir = "SHORT";
                                }
                                else
                                {
                                    rocketDir = "LONG"; // Subida limpia de cuerpo lleno -> Stop & Reverse Compra
                                }
                            }
                        }

                        if (rocketDir == "LONG")
                        {
                            SetStopLoss(CalculationMode.Ticks, RocketStopLossTicks);
                            SetProfitTarget(CalculationMode.Ticks, RocketProfitTargetTicks);
                            EnterLong(Contracts, "BotApertura_Rocket_Long");
                            tradesTodayCount++;
                            barsInPosition = 0;
                            entryTimestamp = Time[0];
                            ActualizarRocketStatusUI($"🚀 COHETE LONG @ {Close[0]} (Modo: {RocketMode})");
                            AppendLog($"[🚀 COHETE V2] GATILLAZO INMEDIATO LONG ({Contracts} cont.) @ {Close[0]} | Modo: {RocketMode} | Mecha Inf: {lowerWickPct:F1}% | EMA Bull: {emaBullish}");
                        }
                        else if (rocketDir == "SHORT")
                        {
                            SetStopLoss(CalculationMode.Ticks, RocketStopLossTicks);
                            SetProfitTarget(CalculationMode.Ticks, RocketProfitTargetTicks);
                            EnterShort(Contracts, "BotApertura_Rocket_Short");
                            tradesTodayCount++;
                            barsInPosition = 0;
                            entryTimestamp = Time[0];
                            ActualizarRocketStatusUI($"🚀 COHETE SHORT @ {Close[0]} (Modo: {RocketMode})");
                            AppendLog($"[🚀 COHETE V2] GATILLAZO INMEDIATO SHORT ({Contracts} cont.) @ {Close[0]} | Modo: {RocketMode} | Mecha Sup: {upperWickPct:F1}% | EMA Bear: {emaBearish}");
                        }
                    }
                }
            }
            if (State == State.Realtime) ActualizarTelemetriaUI();
        }

        #region Ventana Flotante WPF Resizable (Independiente fuera del gráfico)
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
            return new SolidColorBrush(Color.FromArgb(alpha, 15, 23, 42));
        }

        private void InicializarVentanaFlotanteWPF()
        {
            try
            {
                if (controlWindow != null) return;

                controlWindow = new Window
                {
                    Title = "Open Market Control Panel V2 - Bot Apertura 30s",
                    Width = 880,
                    Height = 740,
                    MinWidth = 700,
                    MinHeight = 580,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.CanResizeWithGrip,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    ShowInTaskbar = true,
                    Topmost = false,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                Border outerBorder = new Border
                {
                    Background = HexColor("#0A111E", 245),
                    BorderBrush = HexColor("#1E293B"),
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(14),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };

                Grid rootGrid = new Grid();
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header (Row 0)
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Sec 1 & 2 (Row 1)
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Sec 3 & 4 (Row 2)
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Sec 5: Adaptive Management (Row 3)
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Sec 6: Cohete Recuperación (Row 4)
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Telemetria (Row 5)
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Output Log (Row 6)
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer Buttons (Row 7)

                // ---------------- HEADER ----------------
                Grid headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 10), Cursor = Cursors.SizeAll };
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                headerGrid.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) controlWindow.DragMove(); };

                StackPanel titleStack = new StackPanel { Orientation = Orientation.Horizontal };
                TextBlock title = new TextBlock
                {
                    Text = "⚡ OPEN MARKET CONTROL PANEL 30S (V2) ✥",
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 16,
                    VerticalAlignment = VerticalAlignment.Center
                };
                titleStack.Children.Add(title);
                Grid.SetColumn(titleStack, 0);

                StackPanel headerPills = new StackPanel { Orientation = Orientation.Horizontal };
                btnArmDisarm = new Button
                {
                    Content = "ARMED",
                    Background = HexColor("#052E16"),
                    Foreground = HexColor("#10B981"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(0, 0, 6, 0),
                    BorderBrush = HexColor("#10B981"),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand
                };
                btnArmDisarm.Click += (s, e) => {
                    isArmed = !isArmed;
                    btnArmDisarm.Content = isArmed ? "ARMED" : "DISARMED";
                    btnArmDisarm.Background = isArmed ? HexColor("#052E16") : HexColor("#450A0A");
                    btnArmDisarm.Foreground = isArmed ? HexColor("#10B981") : HexColor("#EF4444");
                    btnArmDisarm.BorderBrush = isArmed ? HexColor("#10B981") : HexColor("#EF4444");
                    AppendLog(isArmed ? "[SYSTEM V2] Estrategia ARMADA" : "[SYSTEM V2] Estrategia DESARMADA");
                };

                txtStatusPill = new TextBlock
                {
                    Text = "LIVE READY V2",
                    Foreground = HexColor("#38BDF8"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 12, 0)
                };

                Button btnCloseWin = new Button
                {
                    Content = " ✕ ",
                    Foreground = Brushes.White,
                    Background = HexColor("#1E293B"),
                    BorderThickness = new Thickness(0),
                    Width = 26, Height = 24,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                btnCloseWin.Click += (s, e) => controlWindow.Hide();

                headerPills.Children.Add(btnArmDisarm);
                headerPills.Children.Add(txtStatusPill);
                headerPills.Children.Add(btnCloseWin);
                Grid.SetColumn(headerPills, 1);

                headerGrid.Children.Add(titleStack);
                headerGrid.Children.Add(headerPills);
                Grid.SetRow(headerGrid, 0);
                rootGrid.Children.Add(headerGrid);

                // ---------------- FILA 1: SEC 1 & SEC 2 ----------------
                Grid row1 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10, GridUnitType.Pixel) });
                row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Sec 1: Scheduled Entry & Profile
                StackPanel s1Stack = new StackPanel();
                s1Stack.Children.Add(CreateKvRow("Hora Disparo NY:", "09:29:58", "#38BDF8"));
                s1Stack.Children.Add(CreateKvRow("Ventana Entrada:", EntryWindowSeconds + " segs", "#FFFFFF"));

                Grid contractsGrid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                contractsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                contractsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                TextBlock lblCont = new TextBlock { Text = "Contratos / Lotes:", Foreground = Brushes.White, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                txtContractsUI = new TextBox
                {
                    Text = Contracts.ToString(),
                    IsReadOnly = true,
                    Background = HexColor("#020710", 220),
                    Foreground = HexColor("#10B981"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Padding = new Thickness(4, 2, 4, 2),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    MinWidth = 80
                };
                txtContractsUI.TextChanged += (s, e) => {
                    if (isUpdatingUI || SelectedProfile != AccountProfileEnum.Custom) return;
                    int val;
                    if (int.TryParse(txtContractsUI.Text, out val) && val > 0)
                    {
                        RecalcularValoresProporcionales(val);
                        ActualizarUIValoresPerfil();
                        AppendLog($"[CUSTOM V2] Recálculo para {Contracts} contratos -> Riesgo: ${DailyMaxLossDollars} | Target: ${DailyProfitTargetDollars} | Loss Cut: ${LossCutDollars}");
                    }
                };
                Grid.SetColumn(lblCont, 0); Grid.SetColumn(txtContractsUI, 1);
                contractsGrid.Children.Add(lblCont); contractsGrid.Children.Add(txtContractsUI);
                s1Stack.Children.Add(contractsGrid);

                Grid profileGrid = new Grid { Margin = new Thickness(0, 4, 0, 2) };
                profileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                profileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                TextBlock lblProf = new TextBlock { Text = "Perfil Cuenta:", Foreground = Brushes.White, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                comboProfile = new ComboBox { Height = 24, FontSize = 11, Background = HexColor("#020710"), Foreground = Brushes.White };
                comboProfile.Items.Add("25K (7 Contratos)");
                comboProfile.Items.Add("50K (15 Contratos)");
                comboProfile.Items.Add("100K (22 Contratos)");
                comboProfile.Items.Add("150K (30 Contratos)");
                comboProfile.Items.Add("CUSTOM (Auto-Escalar por Lotes)");
                comboProfile.SelectedIndex = (int)SelectedProfile;
                comboProfile.SelectionChanged += (s, e) => {
                    AplicarPerfilCuenta((AccountProfileEnum)comboProfile.SelectedIndex);
                };
                Grid.SetColumn(lblProf, 0); Grid.SetColumn(comboProfile, 1);
                profileGrid.Children.Add(lblProf); profileGrid.Children.Add(comboProfile);
                s1Stack.Children.Add(profileGrid);

                Border sec1 = CreateSectionBox("1. SCHEDULED ENTRY & PROFILE", "#38BDF8", s1Stack);
                Grid.SetColumn(sec1, 0);

                // Sec 2: Risk Management
                StackPanel s2Stack = new StackPanel();
                txtStopLossUI = new TextBlock { Text = StopLossTicks + " ticks", Foreground = HexColor("#EF4444"), FontWeight = FontWeights.Bold, FontSize = 11 };
                txtProfitTargetUI = new TextBlock { Text = ProfitTargetTicks + " ticks", Foreground = HexColor("#10B981"), FontWeight = FontWeights.Bold, FontSize = 11 };
                
                s2Stack.Children.Add(CreateKvRowControl("Stop Loss:", txtStopLossUI));
                s2Stack.Children.Add(CreateKvRowControl("Profit Target:", txtProfitTargetUI));

                txtDailyMaxLossUI = new TextBox
                {
                    Text = "-$" + DailyMaxLossDollars.ToString("N0"),
                    IsReadOnly = true,
                    Background = HexColor("#020710", 220),
                    Foreground = HexColor("#EF4444"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Padding = new Thickness(4, 2, 4, 2),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    MinWidth = 90
                };
                s2Stack.Children.Add(CreateKvRowControl("Máx Pérdida Diaria:", txtDailyMaxLossUI));

                txtDailyProfitTargetUI = new TextBox
                {
                    Text = "+$" + DailyProfitTargetDollars.ToString("N0"),
                    IsReadOnly = true,
                    Background = HexColor("#020710", 220),
                    Foreground = HexColor("#10B981"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Padding = new Thickness(4, 2, 4, 2),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    MinWidth = 90
                };
                s2Stack.Children.Add(CreateKvRowControl("Daily Target:", txtDailyProfitTargetUI));

                Border sec2 = CreateSectionBox("2. RISK MANAGEMENT", "#FB923C", s2Stack);
                Grid.SetColumn(sec2, 2);

                row1.Children.Add(sec1); row1.Children.Add(sec2);
                Grid.SetRow(row1, 1); rootGrid.Children.Add(row1);

                // ---------------- FILA 2: SEC 3 & SEC 4 ----------------
                Grid row2 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10, GridUnitType.Pixel) });
                row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Sec 3: Profit Lock 4 Stages
                StackPanel s3Stack = new StackPanel();
                txtStage1UI = new TextBlock { Text = $"Trigger ${Stage1Trigger:N0} → Secure ${Stage1Secure:N0}", Foreground = HexColor("#10B981"), FontWeight = FontWeights.Bold, FontSize = 11 };
                txtStage2UI = new TextBlock { Text = $"Trigger ${Stage2Trigger:N0} → Secure ${Stage2Secure:N0}", Foreground = HexColor("#10B981"), FontWeight = FontWeights.Bold, FontSize = 11 };
                txtStage3UI = new TextBlock { Text = $"Trigger ${Stage3Trigger:N0} → Secure ${Stage3Secure:N0}", Foreground = HexColor("#10B981"), FontWeight = FontWeights.Bold, FontSize = 11 };
                txtStage4UI = new TextBlock { Text = $"Trigger ${Stage4Trigger:N0} → Secure ${Stage4Secure:N0}", Foreground = HexColor("#10B981"), FontWeight = FontWeights.Bold, FontSize = 11 };

                s3Stack.Children.Add(CreateKvRowControl("Stage 1:", txtStage1UI));
                s3Stack.Children.Add(CreateKvRowControl("Stage 2:", txtStage2UI));
                s3Stack.Children.Add(CreateKvRowControl("Stage 3:", txtStage3UI));
                s3Stack.Children.Add(CreateKvRowControl("Stage 4:", txtStage4UI));
                Border sec3 = CreateSectionBox("3. PROFIT LOCK (4 STAGES)", "#34D399", s3Stack);
                Grid.SetColumn(sec3, 0);

                // Sec 4: Pre-Entry Range Guard
                StackPanel s4Stack = new StackPanel();
                s4Stack.Children.Add(CreateKvRow("Ventana Pre-Rango:", "09:00:00 - 09:28:59 NY", "#FFFFFF"));
                s4Stack.Children.Add(CreateKvRow("Umbral Rango:", RangeThresholdPoints + " pts", "#FFFFFF"));
                txtPreRangeInfo = new TextBlock { Text = "Estado Rango: Esperando horario...", Foreground = HexColor("#F59E0B"), FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
                s4Stack.Children.Add(txtPreRangeInfo);
                Border sec4 = CreateSectionBox("4. PRE-ENTRY RANGE GUARD", "#A78BFA", s4Stack);
                Grid.SetColumn(sec4, 2);

                row2.Children.Add(sec3); row2.Children.Add(sec4);
                Grid.SetRow(row2, 2); rootGrid.Children.Add(row2);

                // ---------------- FILA 3: SEC 5 (ADAPTIVE MANAGEMENT & TIMED CUT) ----------------
                Grid adaptiveGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                adaptiveGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                adaptiveGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10, GridUnitType.Pixel) });
                adaptiveGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                adaptiveGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10, GridUnitType.Pixel) });
                adaptiveGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Col 1: Timed Cut Armed Checkbox & Status
                StackPanel adCol1 = new StackPanel();
                chkTimedCutArmedUI = new CheckBox
                {
                    Content = " TIMED CUT - ARMED",
                    IsChecked = TimedCutArmed,
                    Foreground = HexColor("#10B981"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Margin = new Thickness(0, 2, 0, 4)
                };
                chkTimedCutArmedUI.Click += (s, e) => {
                    TimedCutArmed = chkTimedCutArmedUI.IsChecked ?? false;
                    AppendLog(TimedCutArmed ? "[ADAPTIVE V2] Timed Cut ARMADO" : "[ADAPTIVE V2] Timed Cut DESARMADO");
                };
                adCol1.Children.Add(chkTimedCutArmedUI);

                txtAdaptiveStatusUI = new TextBlock { Text = "Estado: Monitoreando 7.5s", Foreground = HexColor("#38BDF8"), FontSize = 10.5 };
                adCol1.Children.Add(txtAdaptiveStatusUI);
                Grid.SetColumn(adCol1, 0);

                // Col 2: Slow RR
                StackPanel adCol2 = new StackPanel();
                txtSlowRRRangeMaxUI = CreateEditableInput(SlowRRRangeMax.ToString("F1"), (val) => SlowRRRangeMax = val);
                txtSlowRRMinHitSecUI = CreateEditableInputInt(SlowRRMinHitSec.ToString(), (val) => SlowRRMinHitSec = val);
                
                adCol2.Children.Add(CreateKvRowControl("Slow RR Max:", txtSlowRRRangeMaxUI));
                adCol2.Children.Add(CreateKvRowControl("Slow Min Sec:", txtSlowRRMinHitSecUI));
                Grid.SetColumn(adCol2, 2);

                // Col 3: Timed Range & Loss Cut
                StackPanel adCol3 = new StackPanel();
                txtTimedRangeMaxUI = CreateEditableInput(TimedRangeMax.ToString("F1"), (val) => TimedRangeMax = val);
                txtEvaluationSecUI = CreateEditableInput(EvaluationSec.ToString("F1"), (val) => EvaluationSec = val);
                txtLossCutUI = CreateEditableInput(LossCutDollars.ToString("N0"), (val) => LossCutDollars = val);

                adCol3.Children.Add(CreateKvRowControl("Timed Range Max:", txtTimedRangeMaxUI));
                adCol3.Children.Add(CreateKvRowControl("Evaluation Sec:", txtEvaluationSecUI));
                adCol3.Children.Add(CreateKvRowControl("Loss Cut $:", txtLossCutUI));
                Grid.SetColumn(adCol3, 4);

                adaptiveGrid.Children.Add(adCol1);
                adaptiveGrid.Children.Add(adCol2);
                adaptiveGrid.Children.Add(adCol3);

                Border sec5 = CreateSectionBox("5. ADAPTIVE MANAGEMENT (TIMED CUT)", "#F43F5E", adaptiveGrid);
                Grid.SetRow(sec5, 3); rootGrid.Children.Add(sec5);

                // ---------------- FILA 4: SEC 6 (ESTRATEGIA COHETE DE RECUPERACIÓN) ----------------
                Grid rocketGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                rocketGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rocketGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10, GridUnitType.Pixel) });
                rocketGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rocketGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10, GridUnitType.Pixel) });
                rocketGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Col 1: Checkbox & Status
                StackPanel rCol1 = new StackPanel();
                chkUseRocketRecoveryUI = new CheckBox
                {
                    Content = " COHETE RECUPERACIÓN - ARMED",
                    IsChecked = UseRocketRecovery,
                    Foreground = HexColor("#F59E0B"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Margin = new Thickness(0, 2, 0, 4)
                };
                chkUseRocketRecoveryUI.Click += (s, e) => {
                    UseRocketRecovery = chkUseRocketRecoveryUI.IsChecked ?? false;
                    AppendLog(UseRocketRecovery ? "[COHETE V2] Recuperación Inmediata ARMADA" : "[COHETE V2] Recuperación Inmediata DESARMADA");
                };
                rCol1.Children.Add(chkUseRocketRecoveryUI);

                txtRocketStatusUI = new TextBlock { Text = "Estado: Listo (Modo Auto)", Foreground = HexColor("#F59E0B"), FontSize = 10.5 };
                rCol1.Children.Add(txtRocketStatusUI);
                Grid.SetColumn(rCol1, 0);

                // Col 2: Modo Disparo Selector & Umbral Mecha
                StackPanel rCol2 = new StackPanel();
                comboRocketModeUI = new ComboBox
                {
                    Background = HexColor("#0F172A"),
                    Foreground = Brushes.White,
                    FontSize = 10.5,
                    Margin = new Thickness(0, 2, 0, 2)
                };
                comboRocketModeUI.Items.Add("Desactivado");
                comboRocketModeUI.Items.Add("Misma Dirección");
                comboRocketModeUI.Items.Add("Inversión (Reverse)");
                comboRocketModeUI.Items.Add("Auto Smart Filter");
                comboRocketModeUI.SelectedIndex = (int)RocketMode;
                comboRocketModeUI.SelectionChanged += (s, e) => {
                    if (isUpdatingUI) return;
                    RocketMode = (RocketRecoveryModeEnum)comboRocketModeUI.SelectedIndex;
                    AppendLog($"[COHETE V2] Modo de Recuperación actualizado a: {RocketMode}");
                };

                txtRocketWickFilterUI = CreateEditableInput(RocketWickFilterPercent.ToString("F0"), (val) => RocketWickFilterPercent = val);

                rCol2.Children.Add(CreateKvRowControl("Modo Disparo:", comboRocketModeUI));
                rCol2.Children.Add(CreateKvRowControl("Mecha VSA Min %:", txtRocketWickFilterUI));
                Grid.SetColumn(rCol2, 2);

                // Col 3: SL Ticks & TP Ticks
                StackPanel rCol3 = new StackPanel();
                txtRocketStopLossUI = CreateEditableInputInt(RocketStopLossTicks.ToString(), (val) => RocketStopLossTicks = val);
                txtRocketProfitTargetUI = CreateEditableInputInt(RocketProfitTargetTicks.ToString(), (val) => RocketProfitTargetTicks = val);

                rCol3.Children.Add(CreateKvRowControl("Cohete SL Ticks:", txtRocketStopLossUI));
                rCol3.Children.Add(CreateKvRowControl("Cohete TP Ticks:", txtRocketProfitTargetUI));
                Grid.SetColumn(rCol3, 4);

                rocketGrid.Children.Add(rCol1);
                rocketGrid.Children.Add(rCol2);
                rocketGrid.Children.Add(rCol3);

                Border sec6 = CreateSectionBox("6. ESTRATEGIA COHETE DE RECUPERACIÓN INMEDIATA (30S)", "#F59E0B", rocketGrid);
                Grid.SetRow(sec6, 4); rootGrid.Children.Add(sec6);

                // ---------------- FILA 5: TELEMETRY ----------------
                Border telemBar = new Border
                {
                    Background = HexColor("#030D1A"),
                    BorderBrush = HexColor("#1E293B"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                Grid tGrid = new Grid();
                tGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                tGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                tGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                tGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                tGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                txtBid = new TextBlock { Text = "Bid: ---", Foreground = Brushes.White, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
                txtAsk = new TextBlock { Text = "Ask: ---", Foreground = Brushes.White, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
                txtMarketTime = new TextBlock { Text = "NY Time: --:--:--", Foreground = HexColor("#38BDF8"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
                txtRealizedPnl = new TextBlock { Text = "Realized: $0.00", Foreground = HexColor("#10B981"), FontWeight = FontWeights.Bold, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
                txtOpenPnl = new TextBlock { Text = "Open: $0.00", Foreground = HexColor("#10B981"), FontWeight = FontWeights.Bold, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };

                Grid.SetColumn(txtBid, 0); Grid.SetColumn(txtAsk, 1); Grid.SetColumn(txtMarketTime, 2); Grid.SetColumn(txtRealizedPnl, 3); Grid.SetColumn(txtOpenPnl, 4);
                tGrid.Children.Add(txtBid); tGrid.Children.Add(txtAsk); tGrid.Children.Add(txtMarketTime); tGrid.Children.Add(txtRealizedPnl); tGrid.Children.Add(txtOpenPnl);
                telemBar.Child = tGrid;
                Grid.SetRow(telemBar, 5); rootGrid.Children.Add(telemBar);

                // ---------------- FILA 6: OUTPUT LOG ----------------
                txtLogOutput = new TextBox
                {
                    IsReadOnly = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Background = HexColor("#020617"),
                    Foreground = HexColor("#38BDF8"),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10.5,
                    Padding = new Thickness(6),
                    Margin = new Thickness(0, 0, 0, 8),
                    BorderBrush = HexColor("#1E293B"),
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                Grid.SetRow(txtLogOutput, 6); rootGrid.Children.Add(txtLogOutput);

                // ---------------- FILA 7: FOOTER BUTTONS ----------------
                Grid fGrid = new Grid();
                fGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                fGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
                fGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                btnFlatten = new Button
                {
                    Content = "FLATTEN & CANCEL ALL",
                    Background = HexColor("#EF4444"),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    Height = 34,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                btnFlatten.Click += (s, e) => EjecutarFlattenYCancelarTodo();
                Grid.SetColumn(btnFlatten, 0);

                Button btnResetPnL = new Button
                {
                    Content = "RESET PnL & METRICS",
                    Background = HexColor("#10B981"),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    Height = 34,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                btnResetPnL.Click += (s, e) => {
                    dailyCumProfit = 0; tradesTodayCount = 0; winsTodayCount = 0; lossesTodayCount = 0; dailyPnLocked = false; currentLockStage = 0;
                    AppendLog("[SYSTEM V2] Métricas de PnL reiniciadas manualmente.");
                    ActualizarTelemetriaUI();
                };
                Grid.SetColumn(btnResetPnL, 2);

                fGrid.Children.Add(btnFlatten); fGrid.Children.Add(btnResetPnL);
                Grid.SetRow(fGrid, 7); rootGrid.Children.Add(fGrid);

                outerBorder.Child = rootGrid;
                controlWindow.Content = outerBorder;
                controlWindow.Show();
                ActualizarUIValoresPerfil();
                AppendLog("[SYSTEM V2] Ventana flotante WPF inicializada correctamente para NewBotAperturaMercadoV2.");
            }
            catch (Exception ex)
            {
                Print("[WPF Window Init ERROR V2] " + ex.Message);
            }
        }

        private TextBox CreateEditableInput(string initialVal, Action<double> onValidUpdate)
        {
            TextBox tb = new TextBox
            {
                Text = initialVal,
                Background = HexColor("#0F172A"),
                Foreground = HexColor("#F43F5E"),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Padding = new Thickness(3, 1, 3, 1),
                HorizontalAlignment = HorizontalAlignment.Right,
                MinWidth = 55
            };
            tb.TextChanged += (s, e) => {
                if (isUpdatingUI) return;
                double val;
                if (double.TryParse(tb.Text.Replace("$", "").Trim(), out val))
                {
                    onValidUpdate(val);
                }
            };
            return tb;
        }

        private TextBox CreateEditableInputInt(string initialVal, Action<int> onValidUpdate)
        {
            TextBox tb = new TextBox
            {
                Text = initialVal,
                Background = HexColor("#0F172A"),
                Foreground = HexColor("#F43F5E"),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Padding = new Thickness(3, 1, 3, 1),
                HorizontalAlignment = HorizontalAlignment.Right,
                MinWidth = 55
            };
            tb.TextChanged += (s, e) => {
                if (isUpdatingUI) return;
                int val;
                if (int.TryParse(tb.Text.Trim(), out val))
                {
                    onValidUpdate(val);
                }
            };
            return tb;
        }

        private Border CreateSectionBox(string titleText, string headerHex, UIElement contentGrid)
        {
            Border b = new Border
            {
                BorderBrush = HexColor(headerHex, 200),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(6),
                Background = HexColor("#040C18", 220),
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
                Background = HexColor("#020710", 220),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            TextBlock val = new TextBlock { Text = valText, Foreground = HexColor(valColorHex), FontWeight = FontWeights.Bold, FontSize = 11 };
            valBox.Child = val;
            Grid.SetColumn(valBox, 1);

            g.Children.Add(lbl); g.Children.Add(valBox);
            return g;
        }

        private UIElement CreateKvRowControl(string labelText, UIElement childControl)
        {
            Grid g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock lbl = new TextBlock { Text = labelText, Foreground = Brushes.White, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lbl, 0);

            Border valBox = new Border
            {
                Background = HexColor("#020710", 220),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 1, 4, 1),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            valBox.Child = childControl;
            Grid.SetColumn(valBox, 1);

            g.Children.Add(lbl); g.Children.Add(valBox);
            return g;
        }

        private void ActualizarAdaptiveStatusUI(string text)
        {
            if (controlWindow == null || controlWindow.Dispatcher == null) return;
            controlWindow.Dispatcher.BeginInvoke(new Action(() => {
                if (txtAdaptiveStatusUI != null) txtAdaptiveStatusUI.Text = "Estado: " + text;
            }), DispatcherPriority.Background);
        }

        private void ActualizarRocketStatusUI(string text)
        {
            if (controlWindow == null || controlWindow.Dispatcher == null) return;
            controlWindow.Dispatcher.BeginInvoke(new Action(() => {
                if (txtRocketStatusUI != null) txtRocketStatusUI.Text = text;
            }), DispatcherPriority.Background);
        }

        private void ActualizarTelemetriaUI()
        {
            if (controlWindow == null || controlWindow.Dispatcher == null) return;
            controlWindow.Dispatcher.BeginInvoke(new Action(() => {
                if (txtBid != null) txtBid.Text = "Bid: " + GetCurrentBid().ToString("F2");
                if (txtAsk != null) txtAsk.Text = "Ask: " + GetCurrentAsk().ToString("F2");
                if (txtMarketTime != null) txtMarketTime.Text = "NY Time: " + DateTime.Now.ToString("HH:mm:ss");
                if (txtRealizedPnl != null) txtRealizedPnl.Text = "Realized: " + (dailyCumProfit >= 0 ? "+" : "") + "$" + dailyCumProfit.ToString("N2");
                if (txtOpenPnl != null && Position != null)
                {
                    double openPnl = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
                    txtOpenPnl.Text = "Open: " + (openPnl >= 0 ? "+" : "") + "$" + openPnl.ToString("N2");
                }
            }), DispatcherPriority.Background);
        }

        private void ActualizarPreRangoUI()
        {
            if (controlWindow == null || controlWindow.Dispatcher == null) return;
            controlWindow.Dispatcher.BeginInvoke(new Action(() => {
                if (txtPreRangeInfo != null)
                    txtPreRangeInfo.Text = $"Rango: {resolvedRangePoints:F2} pts | Dir AUTO: {resolvedDirection}";
            }), DispatcherPriority.Background);
        }

        private void AppendLog(string message)
        {
            string line = $"{DateTime.Now:HH:mm:ss}  {message}";
            Print(line);

            if (controlWindow != null && controlWindow.Dispatcher != null)
            {
                controlWindow.Dispatcher.BeginInvoke(new Action(() => {
                    if (txtLogOutput != null)
                    {
                        txtLogOutput.AppendText(line + "\n");
                        txtLogOutput.ScrollToEnd();
                    }
                }), DispatcherPriority.Background);
            }
        }

        private void CerrarVentanaFlotanteWPF()
        {
            if (controlWindow != null && controlWindow.Dispatcher != null)
            {
                controlWindow.Dispatcher.BeginInvoke(new Action(() => {
                    controlWindow.Close();
                    controlWindow = null;
                }), DispatcherPriority.Normal);
            }
        }

        private void EjecutarFlattenYCancelarTodo()
        {
            try
            {
                if (Account != null && Account.Orders != null)
                {
                    List<Order> workingOrders = new List<Order>();
                    foreach (Order o in Account.Orders)
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
                        Account.Cancel(workingOrders.ToArray());
                        AppendLog($"[FLATTEN V2] Canceladas {workingOrders.Count} órdenes pendientes.");
                    }
                }

                if (Position != null && Position.MarketPosition != MarketPosition.Flat)
                {
                    if (Position.MarketPosition == MarketPosition.Long) ExitLong("BotApertura_Long");
                    else if (Position.MarketPosition == MarketPosition.Short) ExitShort("BotApertura_Short");

                    if (Account != null && Instrument != null)
                    {
                        Account.Flatten(new[] { Instrument });
                    }
                    AppendLog("[FLATTEN V2] Posición liquidada completamente.");
                }
            }
            catch (Exception ex)
            {
                AppendLog("[ERROR FLATTEN V2] " + ex.Message);
            }
        }
        #endregion
    }
}
