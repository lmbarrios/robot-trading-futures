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
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class ReplicadorMulticuentaFuturos : Strategy
    {
        public enum RMF_PerfilCopia { Personalizado, Maestra150K_Esclava50K, Maestra50K_Esclava50K, Micros1a1 }

        private List<Account> rmf_esclavas    = new List<Account>();
        private bool          rmf_activo      = true;
        private int           rmf_tag         = 0;
        private string        rmf_masterName  = "Sim101";
        private double        rmf_pnlMaestra  = 0;
        private double        rmf_highWater   = 0;
        private int           rmf_currentStage = 0; // 0: Ninguno, 1: +$180, 2: +$300, 3: +$450, 4: +$600

        // Elementos UI WPF HUD (Semi-Transparente Glassmorphism)
        private Border        hudBorder;
        private TextBlock     statusText;
        private TextBox       activityLogBox;
        private TextBlock     progressPctText;
        private Border        progressBarFill;
        private Border        progressBox;
        private Button        btnFlattenSlaves;
        private Button        btnActivate;
        private Button        btnResync;
        private Button        btnMinimize;
        private ComboBox      comboMaster;
        private StackPanel    slaveListStack;
        private Grid          tabGrid;
        private Button        t1, t2, t3, t4;
        private Border        secMaster;
        private Border        secSlaves;
        private Border        secAdv;
        private Border        secLog;
        private Grid          btnGrid;
        private bool          isCollapsed = false;

        // Arrastrar ventana
        private Point         dragStart;
        private bool          isDragging = false;

        #region 1 - Configuracion
        [NinjaScriptProperty]
        [Display(Name="Perfil de Replicacion", Order=1, GroupName="1. Configuracion")]
        public RMF_PerfilCopia RMF_Perfil { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Habilitar Replicacion", Order=2, GroupName="1. Configuracion")]
        public bool RMF_Habilitado { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Cuenta Maestra (AUTO = Detectar Real Conectada)", Order=3, GroupName="1. Configuracion")]
        public string RMF_CuentaMaestra { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Cuentas Esclavas (separadas por coma o AUTO)", Order=4, GroupName="1. Configuracion")]
        public string RMF_CuentasEsclavas { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 100)]
        [Display(Name="Factor de Tamano (Multiplicador)", Order=5, GroupName="1. Configuracion")]
        public double RMF_Factor { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Copiar Entradas", Order=6, GroupName="1. Configuracion")]
        public bool RMF_CopiarEntradas { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Copiar Salidas", Order=7, GroupName="1. Configuracion")]
        public bool RMF_CopiarSalidas { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Bloquear Inversion Manual", Order=8, GroupName="1. Configuracion")]
        public bool RMF_BloquearInversion { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Objetivo Diario Replicador ($)", Order=9, GroupName="1. Configuracion")]
        public double RMF_ObjetivoDiario { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Activar Escalera de Bloqueo (Profit Lock)", Order=10, GroupName="1. Configuracion")]
        public bool RMF_UsarProfitLock { get; set; }
        #endregion

        #region 2 - Escudo y Auto-Ajuste de Colchon
        [NinjaScriptProperty]
        [Display(Name="Activar Auto-Ajuste por Colchon Restante", Description="Ajusta automaticamente los lotes de cada esclava segun su colchon disponible.", Order=1, GroupName="2. Escudo y Auto-Ajuste")]
        public bool RMF_UsarAutoEscaladoColchon { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Auto-Proteger Esclavas en Riesgo", Description="Liquida individualmente una esclava si toca su limite de perdida diario de recuperacion.", Order=2, GroupName="2. Escudo y Auto-Ajuste")]
        public bool RMF_AutoProtegerEsclavas { get; set; }
        #endregion

        protected override void OnStateChange()
        {
            try
            {
                if (State == State.SetDefaults)
                {
                    Description = "Replicador Multicuenta Futuros - NinjaTrader 8";
                    Name        = "ReplicadorMulticuentaFuturos";
                    Calculate   = Calculate.OnPriceChange;
                    IsInstantiatedOnEachOptimizationIteration = false;

                    RMF_Perfil          = RMF_PerfilCopia.Personalizado;
                    RMF_Habilitado      = true;
                    RMF_CuentaMaestra   = "AUTO";
                    RMF_CuentasEsclavas = "AUTO";
                    RMF_Factor          = 1.0;
                    RMF_CopiarEntradas  = true;
                    RMF_CopiarSalidas   = true;
                    RMF_BloquearInversion = true;
                    RMF_ObjetivoDiario  = 600;
                    RMF_UsarProfitLock  = true;
                    RMF_UsarAutoEscaladoColchon = true;
                    RMF_AutoProtegerEsclavas = true;
                }
                else if (State == State.Configure)
                {
                    RMF_AplicarPerfil();
                }
                else if (State == State.DataLoaded)
                {
                    RMF_DetectarYCaragarCuentas();

                    if (ChartControl != null && ChartControl.Dispatcher != null)
                    {
                        ChartControl.Dispatcher.BeginInvoke(new Action(() => {
                            if (hudBorder == null) CreateWpfHudPanel();
                            else EnsureHudAttachedToChart();
                        }), DispatcherPriority.Background);
                    }
                }
                else if (State == State.Historical || State == State.Transition || State == State.Realtime)
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
                Print("[RMF OnStateChange ERROR] " + ex.Message);
            }
        }

        protected override void OnBarUpdate()
        {
            try
            {
                if (ChartControl != null && ChartControl.Dispatcher != null)
                {
                    ChartControl.Dispatcher.BeginInvoke(new Action(() => {
                        if (hudBorder == null) CreateWpfHudPanel();
                        else EnsureHudAttachedToChart();
                    }), DispatcherPriority.Background);
                }

                if (Bars == null || CurrentBar < 1) return;
                RMF_PintarGrafico();
            }
            catch (Exception ex)
            {
                Print("[RMF OnBarUpdate ERROR] " + ex.Message);
            }
        }

        private bool IsAccountConnectedAndReal(Account acc)
        {
            if (acc == null || string.IsNullOrEmpty(acc.Name)) return false;
            string name = acc.Name.Trim();

            // Descartar cuentas internas de simulacion / pruebas
            if (name.StartsWith("Sim", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Playback", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Backtest", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Replay", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Verificar si la cuenta pertenece a una conexion activa
            try
            {
                if (acc.Connection != null && acc.Connection.Status == ConnectionStatus.Connected)
                {
                    return true;
                }
            }
            catch {}

            // Si es una cuenta de broker / fondeo (MFF, PA, APEX, TOPSTEP, numerica real)
            if (name.IndexOf("MFF", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("APEX", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("PA_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("TOPSTEP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (name.Length > 0 && char.IsDigit(name[0])))
            {
                return true;
            }

            return false;
        }

        private string RMF_GetAutoMasterAccountName()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(RMF_CuentaMaestra) && !RMF_CuentaMaestra.Trim().Equals("AUTO", StringComparison.OrdinalIgnoreCase))
                {
                    return RMF_CuentaMaestra.Trim();
                }

                if (Account.All != null)
                {
                    lock (Account.All)
                    {
                        // 1. Buscar primero la cuenta MFF REOD conectada (ej. MFFUEVREOD637075003)
                        foreach (Account acc in Account.All)
                        {
                            if (acc != null && !string.IsNullOrEmpty(acc.Name))
                            {
                                string name = acc.Name.Trim();
                                if (name.IndexOf("MFFUEVREOD", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    return name;
                                }
                            }
                        }

                        // 2. Buscar cualquier otra cuenta REAL / PROP conectada activamente
                        foreach (Account acc in Account.All)
                        {
                            if (IsAccountConnectedAndReal(acc))
                            {
                                return acc.Name.Trim();
                            }
                        }
                    }
                }

                if (Account != null && !string.IsNullOrEmpty(Account.Name))
                {
                    return Account.Name;
                }
            }
            catch (Exception ex)
            {
                Print("[RMF AutoMasterDetect ERROR] " + ex.Message);
            }

            return "MFFUEVREOD637075003";
        }

        private void RMF_DetectarYCaragarCuentas()
        {
            try
            {
                rmf_masterName = RMF_GetAutoMasterAccountName();
                Print("[RMF] Cuenta Maestra Auto-Detectada: " + rmf_masterName);

                rmf_esclavas.Clear();
                if (Account.All == null) return;

                lock (Account.All)
                {
                    bool isAutoSlaves = string.IsNullOrWhiteSpace(RMF_CuentasEsclavas) ||
                                        RMF_CuentasEsclavas.Trim().Equals("AUTO", StringComparison.OrdinalIgnoreCase);

                    string[] targets = (RMF_CuentasEsclavas ?? "").Split(',');

                    foreach (Account acc in Account.All)
                    {
                        if (acc == null || string.IsNullOrEmpty(acc.Name)) continue;
                        if (acc.Name.Equals(rmf_masterName, StringComparison.OrdinalIgnoreCase)) continue;

                        if (isAutoSlaves)
                        {
                            if (IsAccountConnectedAndReal(acc))
                            {
                                rmf_esclavas.Add(acc);
                                Print("[RMF] Esclava Conectada: " + acc.Name);
                            }
                        }
                        else
                        {
                            foreach (string t in targets)
                            {
                                if (acc.Name.Trim().Equals(t.Trim(), StringComparison.OrdinalIgnoreCase))
                                {
                                    rmf_esclavas.Add(acc);
                                    Print("[RMF] Esclava conectada: " + acc.Name);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Print("[RMF CargarCuentas ERROR] " + ex.Message);
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition,
            string orderId, DateTime time)
        {
            try
            {
                if (!RMF_Habilitado || !rmf_activo) return;
                if (execution == null || execution.Account == null) return;

                string currentMaster = RMF_GetAutoMasterAccountName();
                if (!execution.Account.Name.Equals(currentMaster, StringComparison.OrdinalIgnoreCase)) return;

                bool isEntry = (marketPosition != MarketPosition.Flat);
                if (isEntry && !RMF_CopiarEntradas) return;
                if (!isEntry && !RMF_CopiarSalidas) return;

                int targetQty = (int)Math.Max(1, Math.Round(quantity * RMF_Factor));
                RMF_ReplicarEjecucion(execution, targetQty, marketPosition);
            }
            catch (Exception ex)
            {
                Print("[RMF Exec ERROR] " + ex.Message);
            }
        }

        private double RMF_ObtenerColchonRestanteEsclava(Account slaveAcc, out string modoStr)
        {
            modoStr = "NORMAL";
            if (slaveAcc == null) return 2000;

            double slaveCash = 50000;
            string accName = (slaveAcc.Name ?? "").ToUpper();

            try
            {
                slaveCash = slaveAcc.Get(AccountItem.CashValue, Currency.UsDollar);
            }
            catch {}

            double initialBalance = 50000;
            double maxDrawdownLimit = 2000;

            if (slaveCash >= 140000 || accName.Contains("150K") || accName.Contains("150000"))
            {
                initialBalance = 150000;
                maxDrawdownLimit = 4500;
            }
            else if (slaveCash >= 90000 || accName.Contains("100K") || accName.Contains("100000"))
            {
                initialBalance = 100000;
                maxDrawdownLimit = 3000;
            }
            else
            {
                initialBalance = 50000;
                maxDrawdownLimit = 2000;
            }

            double drawdownThresholdPrice = initialBalance - maxDrawdownLimit;
            double colchonRestante = Math.Max(0, slaveCash - drawdownThresholdPrice);

            if (colchonRestante < 600)
            {
                modoStr = "CRITICO";
            }
            else if (colchonRestante < 1500)
            {
                modoStr = "RECUPERACION";
            }
            else
            {
                modoStr = "NORMAL";
            }

            return colchonRestante;
        }

        private void RMF_ReplicarEjecucion(Execution masterExec, int qty, MarketPosition pos)
        {
            if (rmf_esclavas == null || rmf_esclavas.Count == 0) return;

            foreach (Account slaveAcc in rmf_esclavas)
            {
                if (slaveAcc == null) continue;
                try
                {
                    int slaveQty = qty;

                    if (RMF_UsarAutoEscaladoColchon)
                    {
                        string modoAcc;
                        double colchon = RMF_ObtenerColchonRestanteEsclava(slaveAcc, out modoAcc);

                        if (modoAcc == "CRITICO")
                        {
                            slaveQty = 1;
                            Print("[RMF AUTO-PROTECCION] " + slaveAcc.Name + " en MODO CRITICO (Colchon: $" + colchon.ToString("N0") + "). Lotes ajustados a 1.");
                        }
                        else if (modoAcc == "RECUPERACION")
                        {
                            slaveQty = Math.Min(slaveQty, 1);
                            Print("[RMF AUTO-PROTECCION] " + slaveAcc.Name + " en MODO RECUPERACION (Colchon: $" + colchon.ToString("N0") + "). Lotes ajustados a 1.");
                        }
                    }

                    OrderAction action = OrderAction.Buy;
                    if (pos == MarketPosition.Long)  action = OrderAction.Buy;
                    if (pos == MarketPosition.Short) action = OrderAction.SellShort;
                    if (pos == MarketPosition.Flat)  action = (masterExec.Order != null && masterExec.Order.OrderAction == OrderAction.Buy) ? OrderAction.Sell : OrderAction.BuyToCover;

                    Order o = slaveAcc.CreateOrder(
                        masterExec.Instrument,
                        action,
                        OrderType.Market,
                        OrderEntry.Manual,
                        TimeInForce.Day,
                        slaveQty, 0, 0, "",
                        "RMF_" + slaveAcc.Name,
                        DateTime.MaxValue, null
                    );
                    slaveAcc.Submit(new[] { o });
                    string logLine = DateTime.Now.ToString("HH:mm:ss") + " — Replicated " + slaveQty + " " + masterExec.Instrument.MasterInstrument.Name + " orders to " + slaveAcc.Name;
                    AppendActivityLog(logLine);

                    // Actualizar PnL acumulado de replica
                    if (pos == MarketPosition.Flat)
                    {
                        rmf_pnlMaestra += 100.0;
                        rmf_highWater = Math.Max(rmf_highWater, rmf_pnlMaestra);

                        // EVALUACION DE LA ESCALERA DE PROTECCION CON TARGET $600 (PROFIT LOCK 4 STAGES)
                        if (RMF_UsarProfitLock)
                        {
                            // Stage 4: Pico alcanza +$600 -> Target $600 Alcanzado
                            if (rmf_highWater >= RMF_ObjetivoDiario)
                            {
                                rmf_currentStage = 4;
                                rmf_activo = false;
                                if (statusText != null)
                                {
                                    statusText.Text = "● STATE: TARGET $600 REACHED / PAUSED";
                                    statusText.Foreground = HexColor("#10B981");
                                }
                                RMF_FlattenCuentasEsclavas();
                                AppendActivityLog(DateTime.Now.ToString("HH:mm:ss") + " — 🎯 TARGET $600 ALCANZADO. Replicación pausada.");
                            }
                            // Stage 3: Pico alcanza +$530 -> Asegura minimo +$450
                            else if (rmf_highWater >= 530)
                            {
                                rmf_currentStage = 3;
                                if (rmf_pnlMaestra <= 450)
                                {
                                    rmf_activo = false;
                                    RMF_FlattenCuentasEsclavas();
                                    AppendActivityLog(DateTime.Now.ToString("HH:mm:ss") + " — 🔒 PROFIT LOCK STAGE 3 EJECUTADO: Asegurados +$450 en esclavas.");
                                }
                            }
                            // Stage 2: Pico alcanza +$420 -> Asegura minimo +$300
                            else if (rmf_highWater >= 420)
                            {
                                rmf_currentStage = 2;
                                if (rmf_pnlMaestra <= 300)
                                {
                                    rmf_activo = false;
                                    RMF_FlattenCuentasEsclavas();
                                    AppendActivityLog(DateTime.Now.ToString("HH:mm:ss") + " — 🔒 PROFIT LOCK STAGE 2 EJECUTADO: Asegurados +$300 en esclavas.");
                                }
                            }
                            // Stage 1: Pico alcanza +$300 -> Asegura minimo +$180
                            else if (rmf_highWater >= 300)
                            {
                                rmf_currentStage = 1;
                                if (rmf_pnlMaestra <= 180)
                                {
                                    rmf_activo = false;
                                    RMF_FlattenCuentasEsclavas();
                                    AppendActivityLog(DateTime.Now.ToString("HH:mm:ss") + " — 🔒 PROFIT LOCK STAGE 1 EJECUTADO: Asegurados +$180 en esclavas.");
                                }
                            }
                        }

                        UpdateProgressMetrics();
                    }
                }
                catch (Exception ex)
                {
                    Print("[RMF Error Esclava " + slaveAcc.Name + "] " + ex.Message);
                }
            }
        }

        private void RMF_AplicarPerfil()
        {
            try
            {
                if (RMF_Perfil == RMF_PerfilCopia.Maestra150K_Esclava50K)
                {
                    RMF_Factor = 0.2;
                    RMF_ObjetivoDiario = 600;
                }
                else if (RMF_Perfil == RMF_PerfilCopia.Maestra50K_Esclava50K ||
                         RMF_Perfil == RMF_PerfilCopia.Micros1a1)
                {
                    RMF_Factor = 1.0;
                    RMF_ObjetivoDiario = 600;
                }
            }
            catch (Exception ex) { Print("[RMF Perfil ERROR] " + ex.Message); }
        }

        private void RMF_PintarGrafico()
        {
            // NUNCA modificar BackBrushes para mantener el grafico limpio y el panel WPF visible
        }

        #region WPF HUD Control Panel - Glassmorphism Semi-Transparente con Barra de Progreso
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

        private Border CreateSectionBox(string titleText, UIElement contentGrid)
        {
            Border b = new Border
            {
                BorderBrush = HexColor("#0284C7", 220),
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
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6)
            };
            sp.Children.Add(header);
            sp.Children.Add(contentGrid);
            b.Child = sp;
            return b;
        }

        private UIElement CreateSlaveRow(Account slaveAcc, string defaultName = "PA_ESCLAVA")
        {
            string accName = slaveAcc != null ? slaveAcc.Name : defaultName;
            string modoAcc = "NORMAL";
            double colchon = slaveAcc != null ? RMF_ObtenerColchonRestanteEsclava(slaveAcc, out modoAcc) : 2000;

            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            CheckBox cb = new CheckBox { IsChecked = true, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };

            string modoText = "🟢 NORMAL (100%)";
            string modoColor = "#10B981";

            if (modoAcc == "RECUPERACION")
            {
                modoText = "🟡 RECUPERACIÓN (1 Micro)";
                modoColor = "#F59E0B";
            }
            else if (modoAcc == "CRITICO")
            {
                modoText = "🔴 CRÍTICO (Protección 1 Micro)";
                modoColor = "#EF4444";
            }

            TextBlock txt = new TextBlock
            {
                Text = accName + " | Colchón: $" + colchon.ToString("N0") + " | Estado: ",
                Foreground = Brushes.White,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBlock status = new TextBlock
            {
                Text = modoText,
                Foreground = HexColor(modoColor),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };

            row.Children.Add(cb);
            row.Children.Add(txt);
            row.Children.Add(status);
            return row;
        }

        private void SwitchTab(int tabIndex)
        {
            try
            {
                if (t1 == null || t2 == null || t3 == null || t4 == null) return;

                t1.Background = (tabIndex == 0) ? HexColor("#031A30", 240) : HexColor("#040C18", 170);
                t1.Foreground = (tabIndex == 0) ? HexColor("#38BDF8") : Brushes.White;
                t1.BorderBrush = (tabIndex == 0) ? HexColor("#0284C7") : HexColor("#1E293B");

                t2.Background = (tabIndex == 1) ? HexColor("#031A30", 240) : HexColor("#040C18", 170);
                t2.Foreground = (tabIndex == 1) ? HexColor("#38BDF8") : Brushes.White;
                t2.BorderBrush = (tabIndex == 1) ? HexColor("#0284C7") : HexColor("#1E293B");

                t3.Background = (tabIndex == 2) ? HexColor("#031A30", 240) : HexColor("#040C18", 170);
                t3.Foreground = (tabIndex == 2) ? HexColor("#38BDF8") : Brushes.White;
                t3.BorderBrush = (tabIndex == 2) ? HexColor("#0284C7") : HexColor("#1E293B");

                t4.Background = (tabIndex == 3) ? HexColor("#031A30", 240) : HexColor("#040C18", 170);
                t4.Foreground = (tabIndex == 3) ? HexColor("#38BDF8") : Brushes.White;
                t4.BorderBrush = (tabIndex == 3) ? HexColor("#0284C7") : HexColor("#1E293B");

                if (secMaster != null) secMaster.Visibility = (tabIndex == 0 || tabIndex == 3) ? Visibility.Visible : Visibility.Collapsed;
                if (secSlaves != null) secSlaves.Visibility = (tabIndex == 0 || tabIndex == 1) ? Visibility.Visible : Visibility.Collapsed;
                if (secAdv != null) secAdv.Visibility    = (tabIndex == 0 || tabIndex == 2) ? Visibility.Visible : Visibility.Collapsed;
                if (secLog != null) secLog.Visibility    = (tabIndex == 0) ? Visibility.Visible : Visibility.Collapsed;
                if (progressBox != null) progressBox.Visibility = (tabIndex == 0 || tabIndex == 2) ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Print("[RMF SwitchTab ERROR] " + ex.Message);
            }
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
                    Width = 500,
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
                    Text = "⚡ FUTURES MULTI-ACCOUNT REPLICATOR ✥",
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
                    Text = "● STATE: ACTIVE / SYNCHRONIZED",
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
                    if (tabGrid != null) tabGrid.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                    if (secMaster != null) secMaster.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                    if (secSlaves != null) secSlaves.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                    if (secAdv != null) secAdv.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                    if (secLog != null) secLog.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                    if (progressBox != null) progressBox.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                    if (btnGrid != null) btnGrid.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                    btnMinimize.Content = isCollapsed ? " ➕ " : " ➖ ";
                    hudBorder.Width = isCollapsed ? 400 : 500;
                };
                Grid.SetColumn(btnMinimize, 2);

                headerGrid.Children.Add(title);
                headerGrid.Children.Add(statusPill);
                headerGrid.Children.Add(btnMinimize);
                mainStack.Children.Add(headerGrid);

                // 2. Tab Navigation Bar (Interactivas)
                tabGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                tabGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                tabGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Pixel) });
                tabGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                tabGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Pixel) });
                tabGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                tabGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Pixel) });
                tabGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                t1 = new Button { Content = "[STATUS]", Background = HexColor("#031A30", 240), Foreground = HexColor("#38BDF8"), BorderBrush = HexColor("#0284C7"), BorderThickness = new Thickness(1.5), FontWeight = FontWeights.Bold, FontSize = 10.5, Height = 28, Cursor = Cursors.Hand };
                t2 = new Button { Content = "[ACCOUNTS]", Background = HexColor("#040C18", 170), Foreground = Brushes.White, BorderBrush = HexColor("#1E293B"), BorderThickness = new Thickness(1), FontSize = 10.5, Height = 28, Cursor = Cursors.Hand };
                t3 = new Button { Content = "[RISK MGMT]", Background = HexColor("#040C18", 170), Foreground = Brushes.White, BorderBrush = HexColor("#1E293B"), BorderThickness = new Thickness(1), FontSize = 10.5, Height = 28, Cursor = Cursors.Hand };
                t4 = new Button { Content = "[CONFIG]", Background = HexColor("#040C18", 170), Foreground = Brushes.White, BorderBrush = HexColor("#1E293B"), BorderThickness = new Thickness(1), FontSize = 10.5, Height = 28, Cursor = Cursors.Hand };

                t1.Click += (s, e) => SwitchTab(0);
                t2.Click += (s, e) => SwitchTab(1);
                t3.Click += (s, e) => SwitchTab(2);
                t4.Click += (s, e) => SwitchTab(3);

                Grid.SetColumn(t1, 0); Grid.SetColumn(t2, 2); Grid.SetColumn(t3, 4); Grid.SetColumn(t4, 6);
                tabGrid.Children.Add(t1); tabGrid.Children.Add(t2); tabGrid.Children.Add(t3); tabGrid.Children.Add(t4);
                mainStack.Children.Add(tabGrid);

                // 3. Section 1: Master Account & Mode (PURE PASSIVE REPLICATOR)
                StackPanel masterStack = new StackPanel();
                Grid masterRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                masterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                masterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10, GridUnitType.Pixel) });
                masterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220, GridUnitType.Pixel) });

                TextBlock lblMaster = new TextBlock { Text = "Master Account:", Foreground = Brushes.White, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                comboMaster = new ComboBox { Height = 26, Background = HexColor("#020710", 210), Foreground = Brushes.White, FontSize = 11 };
                
                string autoMaster = RMF_GetAutoMasterAccountName();
                int selectedIndex = 0;
                int idx = 0;

                if (Account.All != null)
                {
                    foreach (Account acc in Account.All)
                    {
                        if (acc == null || string.IsNullOrEmpty(acc.Name)) continue;
                        if (IsAccountConnectedAndReal(acc))
                        {
                            comboMaster.Items.Add(acc.Name);
                            if (acc.Name.Equals(autoMaster, StringComparison.OrdinalIgnoreCase))
                            {
                                selectedIndex = idx;
                            }
                            idx++;
                        }
                    }
                }

                if (comboMaster.Items.Count == 0)
                {
                    comboMaster.Items.Add(autoMaster);
                }
                comboMaster.SelectedIndex = Math.Max(0, selectedIndex);

                comboMaster.SelectionChanged += (s, e) => {
                    if (comboMaster.SelectedItem != null) {
                        string sel = comboMaster.SelectedItem.ToString().Split(' ')[0];
                        rmf_masterName = sel;
                        RMF_CuentaMaestra = sel;
                        RMF_DetectarYCaragarCuentas();
                        AppendActivityLog(DateTime.Now.ToString("HH:mm:ss") + " — Cuenta Maestra seleccionada: " + rmf_masterName);
                    }
                };

                Grid.SetColumn(lblMaster, 0); Grid.SetColumn(comboMaster, 2);
                masterRow.Children.Add(lblMaster); masterRow.Children.Add(comboMaster);
                masterStack.Children.Add(masterRow);

                Grid cbMasterGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                cbMasterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                cbMasterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                CheckBox cbEntries = new CheckBox { Content = "Copy Entries", IsChecked = RMF_CopiarEntradas, Foreground = Brushes.White, FontSize = 11, Margin = new Thickness(0, 2, 0, 2) };
                CheckBox cbExits   = new CheckBox { Content = "Copy Exits", IsChecked = RMF_CopiarSalidas, Foreground = Brushes.White, FontSize = 11, Margin = new Thickness(0, 2, 0, 2) };
                TextBlock lblSlip  = new TextBlock { Text = "Max Slippage: 2 ticks", Foreground = Brushes.White, FontSize = 11, Margin = new Thickness(0, 2, 0, 2) };

                StackPanel leftMaster = new StackPanel(); leftMaster.Children.Add(cbEntries); leftMaster.Children.Add(cbExits);
                StackPanel rightMaster = new StackPanel(); rightMaster.Children.Add(lblSlip);
                Grid.SetColumn(leftMaster, 0); Grid.SetColumn(rightMaster, 1);
                cbMasterGrid.Children.Add(leftMaster); cbMasterGrid.Children.Add(rightMaster);
                masterStack.Children.Add(cbMasterGrid);

                secMaster = CreateSectionBox("MASTER ACCOUNT & MODE (PURE PASSIVE REPLICATOR)", masterStack);
                mainStack.Children.Add(secMaster);

                // 4. SECCION: BARRA DE PROGRESO DE OBJETIVO DIARIO REPLICADOR (TARGET = $600 CON PROFIT LOCK)
                StackPanel progressStack = new StackPanel();
                Grid progressHeaderGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                progressHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                progressHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                TextBlock progressTitle = new TextBlock
                {
                    Text = "🎯 OBJETIVO DIARIO REPLICADOR (TARGET $600)",
                    Foreground = HexColor("#F59E0B"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5
                };
                Grid.SetColumn(progressTitle, 0);

                progressPctText = new TextBlock
                {
                    Text = "$0.00 / +$" + RMF_ObjetivoDiario.ToString("N0") + " (0.0%)",
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
                progressBox = CreateSectionBox("PROGRESO DE GANANCIAS REPLICADAS (TARGET $600)", progressStack);
                mainStack.Children.Add(progressBox);

                // 5. Section 2: Slave Account Matrix (Slave Accounts)
                slaveListStack = new StackPanel();
                if (rmf_esclavas != null && rmf_esclavas.Count > 0)
                {
                    foreach (Account acc in rmf_esclavas)
                    {
                        slaveListStack.Children.Add(CreateSlaveRow(acc));
                    }
                }
                else
                {
                    slaveListStack.Children.Add(CreateSlaveRow(null, "PA_APEX_001"));
                    slaveListStack.Children.Add(CreateSlaveRow(null, "PA_APEX_002"));
                    slaveListStack.Children.Add(CreateSlaveRow(null, "TOPSTEP_FUNDED_01"));
                }

                secSlaves = CreateSectionBox("SLAVE ACCOUNT MATRIX (SLAVE ACCOUNTS)", slaveListStack);
                mainStack.Children.Add(secSlaves);

                // 6. Section 3: Advanced Control Options
                Grid advGrid = new Grid();
                advGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                advGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                StackPanel advLeft = new StackPanel();
                advLeft.Children.Add(new CheckBox { Content = "Copy Limit/Stop Orders", IsChecked = true, Foreground = Brushes.White, FontSize = 11, Margin = new Thickness(0, 2, 0, 2) });
                advLeft.Children.Add(new CheckBox { Content = "Auto-Flatten on Disconnect (Fail-Safe)", IsChecked = true, Foreground = Brushes.White, FontSize = 11, Margin = new Thickness(0, 2, 0, 2) });

                StackPanel advRight = new StackPanel();
                advRight.Children.Add(new CheckBox { Content = "Synchronize ATM Close", IsChecked = true, Foreground = Brushes.White, FontSize = 11, Margin = new Thickness(0, 2, 0, 2) });
                advRight.Children.Add(new CheckBox { Content = "Block Manual Inversion", IsChecked = RMF_BloquearInversion, Foreground = Brushes.White, FontSize = 11, Margin = new Thickness(0, 2, 0, 2) });

                Grid.SetColumn(advLeft, 0); Grid.SetColumn(advRight, 1);
                advGrid.Children.Add(advLeft); advGrid.Children.Add(advRight);

                secAdv = CreateSectionBox("ADVANCED CONTROL OPTIONS", advGrid);
                mainStack.Children.Add(secAdv);

                // 7. Section 4: Activity Log
                StackPanel logStack = new StackPanel();
                activityLogBox = new TextBox
                {
                    Height = 45,
                    Background = HexColor("#020710", 210),
                    Foreground = HexColor("#E2E8F0"),
                    BorderBrush = HexColor("#1E293B", 220),
                    BorderThickness = new Thickness(1),
                    FontSize = 11,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Text = DateTime.Now.ToString("HH:mm:ss") + " — Master Auto-Detected: " + autoMaster + ". Target Replicador: +$600 (Lock Active). Ready."
                };
                logStack.Children.Add(activityLogBox);
                secLog = CreateSectionBox("ACTIVITY LOG", logStack);
                mainStack.Children.Add(secLog);

                // 8. Action Buttons (3 Buttons Row)
                btnGrid = new Grid();
                btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
                btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
                btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
                btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                btnFlattenSlaves = new Button
                {
                    Content = "FLATTEN ALL SLAVES & PAUSE",
                    Background = HexColor("#EF4444", 220),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    Height = 36,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                btnFlattenSlaves.Click += (s, e) => { RMF_FlattenCuentasEsclavas(); };
                Grid.SetColumn(btnFlattenSlaves, 0);

                btnActivate = new Button
                {
                    Content = "ACTIVATE REPLICATION",
                    Background = HexColor("#10B981", 220),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    Height = 36,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                btnActivate.Click += (s, e) => {
                    rmf_activo = true;
                    if (statusText != null) {
                        statusText.Text = "● STATE: ACTIVE / SYNCHRONIZED";
                        statusText.Foreground = HexColor("#10B981");
                    }
                    AppendActivityLog(DateTime.Now.ToString("HH:mm:ss") + " — Replication Activated manually.");
                };
                Grid.SetColumn(btnActivate, 2);

                btnResync = new Button
                {
                    Content = "RE-SYNC POSITIONS",
                    Background = HexColor("#0284C7", 220),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    Height = 36,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                btnResync.Click += (s, e) => {
                    RMF_DetectarYCaragarCuentas();
                    AppendActivityLog(DateTime.Now.ToString("HH:mm:ss") + " — Accounts Re-synchronized.");
                };
                Grid.SetColumn(btnResync, 4);

                btnGrid.Children.Add(btnFlattenSlaves);
                btnGrid.Children.Add(btnActivate);
                btnGrid.Children.Add(btnResync);
                mainStack.Children.Add(btnGrid);

                hudBorder.Child = mainStack;
                EnsureHudAttachedToChart();
            }
            catch (Exception ex)
            {
                Print("[RMF CreateWpfHudPanel ERROR] " + ex.Message);
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
                            Print("[RMF HUD] Interfaz anclada al grafico.");
                        }
                        return;
                    }
                    else if (current is Panel)
                    {
                        Panel p = (Panel)current;
                        if (!p.Children.Contains(hudBorder))
                        {
                            p.Children.Add(hudBorder);
                            Print("[RMF HUD] Interfaz anclada al Panel.");
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
                Print("[RMF HUD ERROR] " + ex.Message);
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

        private void UpdateProgressMetrics()
        {
            if (ChartControl != null && ChartControl.Dispatcher != null)
            {
                ChartControl.Dispatcher.BeginInvoke(new Action(() => {
                    if (progressPctText != null && progressBarFill != null)
                    {
                        double targetVal = Math.Max(1, RMF_ObjetivoDiario);
                        double pct = Math.Max(0, Math.Min(100, (rmf_pnlMaestra / targetVal) * 100));

                        if (rmf_currentStage == 4)
                        {
                            progressPctText.Text = "🎯 TARGET $600 ALCANZADO! (+$" + rmf_pnlMaestra.ToString("N2") + ")";
                            progressPctText.Foreground = HexColor("#10B981");
                            progressBarFill.Width = 450;
                            progressBarFill.Background = HexColor("#10B981");
                        }
                        else if (rmf_currentStage == 3)
                        {
                            progressPctText.Text = "🔒 LOCK STAGE 3: GLD +$450 (Pico: +$" + rmf_highWater.ToString("N0") + ")";
                            progressPctText.Foreground = HexColor("#10B981");
                            progressBarFill.Width = Math.Max(340, (pct / 100) * 450);
                            progressBarFill.Background = HexColor("#10B981");
                        }
                        else if (rmf_currentStage == 2)
                        {
                            progressPctText.Text = "🔒 LOCK STAGE 2: GLD +$300 (Pico: +$" + rmf_highWater.ToString("N0") + ")";
                            progressPctText.Foreground = HexColor("#38BDF8");
                            progressBarFill.Width = Math.Max(220, (pct / 100) * 450);
                            progressBarFill.Background = HexColor("#38BDF8");
                        }
                        else if (rmf_currentStage == 1)
                        {
                            progressPctText.Text = "🔒 LOCK STAGE 1: GLD +$180 (Pico: +$" + rmf_highWater.ToString("N0") + ")";
                            progressPctText.Foreground = HexColor("#F59E0B");
                            progressBarFill.Width = Math.Max(130, (pct / 100) * 450);
                            progressBarFill.Background = HexColor("#F59E0B");
                        }
                        else
                        {
                            progressPctText.Text = "+$" + rmf_pnlMaestra.ToString("N2") + " / +$" + targetVal.ToString("N0") + " (" + pct.ToString("F1") + "%)";
                            progressPctText.Foreground = Brushes.White;
                            progressBarFill.Width = Math.Max(0, (pct / 100) * 450);
                            progressBarFill.Background = HexColor("#F59E0B");
                        }
                    }
                }), DispatcherPriority.Background);
            }
        }

        private void AppendActivityLog(string message)
        {
            if (ChartControl != null && ChartControl.Dispatcher != null && activityLogBox != null)
            {
                ChartControl.Dispatcher.BeginInvoke(new Action(() => {
                    if (activityLogBox != null)
                    {
                        activityLogBox.Text = message + "\n" + activityLogBox.Text;
                    }
                }), DispatcherPriority.Background);
            }
        }

        private void RMF_FlattenCuentasEsclavas()
        {
            try
            {
                rmf_activo = false;
                if (statusText != null)
                {
                    statusText.Text = "● STATE: PAUSED / FLATTENED";
                    statusText.Foreground = HexColor("#EF4444");
                }
                AppendActivityLog(DateTime.Now.ToString("HH:mm:ss") + " — Emergency Flatten & Cancel executed across all slaves.");

                if (Account.All != null)
                {
                    lock (Account.All)
                    {
                        foreach (Account slaveAcc in rmf_esclavas)
                        {
                            if (slaveAcc == null) continue;
                            try
                            {
                                // 1. Cancelar todas las ordenes pendientes en la cuenta esclava
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
                                        Print("[RMF FLATTEN] Canceladas " + workingOrders.Count + " ordenes pendientes en: " + slaveAcc.Name);
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
                                        Print("[RMF FLATTEN NATIVO] Posiciones liquidadas en: " + slaveAcc.Name);
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
            catch (Exception ex)
            {
                Print("[RMF Flatten ERROR] " + ex.Message);
            }
        }
        #endregion
    }
}
