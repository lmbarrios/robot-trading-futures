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

        #region 1 - Configuracion
        [NinjaScriptProperty]
        [Display(Name="Perfil de Replicacion", Order=1, GroupName="1. Configuracion")]
        public RMF_PerfilCopia RMF_Perfil { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Habilitar Replicacion", Order=2, GroupName="1. Configuracion")]
        public bool RMF_Habilitado { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Cuenta Maestra (nombre exacto)", Order=3, GroupName="1. Configuracion")]
        public string RMF_CuentaMaestra { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Cuentas Esclavas (coma o AUTO)", Order=4, GroupName="1. Configuracion")]
        public string RMF_CuentasEsclavas { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Factor Multiplicacion", Order=5, GroupName="1. Configuracion")]
        public double RMF_Factor { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Replicar Entradas", Order=6, GroupName="1. Configuracion")]
        public bool RMF_CopiarEntradas { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Replicar Salidas", Order=7, GroupName="1. Configuracion")]
        public bool RMF_CopiarSalidas { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Bloquear Inversion Posicion", Order=8, GroupName="1. Configuracion")]
        public bool RMF_BloquearInversion { get; set; }
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
                    IsInstantiatedOnEachOptimizationProperty = false;

                    RMF_Perfil          = RMF_PerfilCopia.Personalizado;
                    RMF_Habilitado      = true;
                    RMF_CuentaMaestra   = "Sim101";
                    RMF_CuentasEsclavas = "AUTO";
                    RMF_Factor          = 1.0;
                    RMF_CopiarEntradas  = true;
                    RMF_CopiarSalidas   = true;
                    RMF_BloquearInversion = true;
                }
                else if (State == State.Configure)
                {
                    RMF_AplicarPerfil();
                }
                else if (State == State.DataLoaded)
                {
                    RMF_CargarCuentas();
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
                if (Bars == null || CurrentBar < 1) return;
                RMF_PintarGrafico();
            }
            catch (Exception ex)
            {
                Print("[RMF OnBarUpdate ERROR] " + ex.Message);
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition,
            string orderId, DateTime time)
        {
            try
            {
                if (!RMF_Habilitado || !rmf_activo) return;
                if (execution == null || execution.Account == null || execution.Account.Name == null) return;
                if (Instrument == null) return;

                string masterName = (RMF_CuentaMaestra ?? "").Trim();
                if (!execution.Account.Name.Equals(masterName, StringComparison.OrdinalIgnoreCase)) return;

                int escQty = (int)Math.Max(1, Math.Round(quantity * RMF_Factor));

                foreach (Account esc in rmf_esclavas)
                {
                    try
                    {
                        if (esc == null || esc.Name == null) continue;
                        bool esCompra = execution.Order != null &&
                            (execution.Order.OrderAction == OrderAction.Buy ||
                             execution.Order.OrderAction == OrderAction.BuyToCover);

                        OrderAction accion = esCompra ? OrderAction.Buy : OrderAction.Sell;

                        if (RMF_CopiarEntradas)
                        {
                            Order ord = esc.CreateOrder(
                                Instrument, accion, OrderType.Market, OrderEntry.Manual,
                                TimeInForce.Gtc, escQty, 0, 0, "",
                                "RMF_Copia", DateTime.MaxValue, null);
                            if (ord != null)
                            {
                                esc.Submit(new[] { ord });
                                Print("[RMF] Replicado en " + esc.Name + " qty:" + escQty);
                            }
                        }
                    }
                    catch (Exception ex2)
                    {
                        Print("[RMF Replica ERROR] " + (esc != null ? esc.Name : "null") + ": " + ex2.Message);
                    }
                }

                if (Bars != null && CurrentBar >= 1)
                {
                    rmf_tag++;
                    bool esLong = (marketPosition == MarketPosition.Long);
                    if (esLong)
                        Draw.ArrowUp(this, "RMF_C_" + rmf_tag, false, 0, Low[0] - TickSize * 6, Brushes.LightGreen);
                    else if (marketPosition == MarketPosition.Short)
                        Draw.ArrowDown(this, "RMF_V_" + rmf_tag, false, 0, High[0] + TickSize * 6, Brushes.Tomato);
                }
            }
            catch (Exception ex)
            {
                Print("[RMF OnExecutionUpdate ERROR] " + ex.Message);
            }
        }

        private void RMF_CargarCuentas()
        {
            try
            {
                rmf_esclavas.Clear();
                string master = (RMF_CuentaMaestra ?? "").Trim();
                string slaves = (RMF_CuentasEsclavas ?? "").Trim();
                bool autoDetect = string.IsNullOrEmpty(slaves) ||
                    slaves.Equals("AUTO", StringComparison.OrdinalIgnoreCase) ||
                    slaves.Equals("TODAS", StringComparison.OrdinalIgnoreCase);

                string[] nombres = autoDetect ? new string[0] : slaves.Split(',');

                if (Account.All != null)
                {
                    lock (Account.All)
                    {
                        foreach (Account acc in Account.All)
                        {
                            if (acc == null || acc.Name == null) continue;
                            if (acc.Name.Equals(master, StringComparison.OrdinalIgnoreCase)) continue;

                            if (autoDetect)
                            {
                                rmf_esclavas.Add(acc);
                                Print("[RMF] Esclava detectada: " + acc.Name);
                            }
                            else
                            {
                                foreach (string n in nombres)
                                    if (acc.Name.Trim().Equals(n.Trim(), StringComparison.OrdinalIgnoreCase))
                                    {
                                        rmf_esclavas.Add(acc);
                                        Print("[RMF] Esclava registrada: " + acc.Name);
                                    }
                            }
                        }
                    }
                }
                Print("[RMF] Total esclavas: " + rmf_esclavas.Count);
            }
            catch (Exception ex)
            {
                Print("[RMF CargarCuentas ERROR] " + ex.Message);
            }
        }

        private void RMF_AplicarPerfil()
        {
            try
            {
                if (RMF_Perfil == RMF_PerfilCopia.Maestra150K_Esclava50K)
                    RMF_Factor = 0.2;
                else if (RMF_Perfil == RMF_PerfilCopia.Maestra50K_Esclava50K ||
                         RMF_Perfil == RMF_PerfilCopia.Micros1a1)
                    RMF_Factor = 1.0;
            }
            catch (Exception ex) { Print("[RMF Perfil ERROR] " + ex.Message); }
        }

        private void RMF_PintarGrafico()
        {
            try
            {
                if (BackBrushes == null || BackBrushes.Length == 0) return;
                if (Bars == null || CurrentBar < 1) return;

                if (!RMF_Habilitado || !rmf_activo)
                {
                    BackBrushes[0] = Brushes.Orange;
                    return;
                }

                BackBrushes[0] = Brushes.DarkGreen;
            }
            catch (Exception ex)
            {
                Print("[RMF Pintar ERROR] " + ex.Message);
            }
        }
    }
}
