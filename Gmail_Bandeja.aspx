<%@ Page Title="Bandeja Gmail" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" Async="true" AsyncTimeout="600" CodeBehind="Gmail_Bandeja.aspx.cs" Inherits="RecepcionDocumental.Gmail_Bandeja" %>
<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">
<main class="gmail-inbox-page">
    <header class="page-header d-flex justify-content-between align-items-end flex-wrap gap-3">
        <div>
            <p class="eyebrow">Gmail</p>
            <h1>Bandeja de entrada</h1>
        </div>
        <div class="d-flex gap-2">
            <asp:Button ID="btnBuscar" runat="server" Text="Buscar nuevos correos" CssClass="btn btn-primary" UseSubmitBehavior="false" OnClientClick="this.disabled=true;this.value='Buscando…';" OnClick="Buscar_Click" />
            <a href="Gmail_Config.aspx" class="btn btn-outline-primary">Configurar cuenta</a>
        </div>
    </header>

    <asp:Panel ID="pnlDatabaseWarning" runat="server" Visible="false" CssClass="alert alert-warning">
        <asp:Literal ID="litDatabaseWarning" runat="server" />
    </asp:Panel>

    <p class="text-secondary mb-3"><asp:Literal ID="litSyncStatus" runat="server" /></p>

    <asp:Panel ID="pnlSinCuenta" runat="server" Visible="false" CssClass="alert alert-info">
        No hay una cuenta Gmail activa. <a href="Gmail_Config.aspx">Configurar cuenta</a>.
    </asp:Panel>

    <asp:Panel ID="pnlResultado" runat="server" Visible="false" CssClass="alert alert-success">
        <h2 class="h5">Sincronización terminada</h2>
        <ul class="mb-0">
            <li>Mensajes encontrados: <asp:Literal ID="litEncontrados" runat="server" /></li>
            <li>Mensajes relevantes nuevos: <asp:Literal ID="litNuevos" runat="server" /></li>
            <li>Adjuntos analizados: <asp:Literal ID="litAnalizados" runat="server" /></li>
            <li>Facturas detectadas: <asp:Literal ID="litFacturas" runat="server" /></li>
            <li>Para revisar: <asp:Literal ID="litRevisar" runat="server" /></li>
            <li>Descartados: <asp:Literal ID="litDescartados" runat="server" /></li>
            <li>Documentos existentes: <asp:Literal ID="litDocumentosExistentes" runat="server" /></li>
            <li>Errores: <asp:Literal ID="litErrores" runat="server" /></li>
        </ul>
        <asp:Literal ID="litFallback" runat="server" />
    </asp:Panel>

    <section class="gmail-filter-card mb-3" aria-label="Filtros de bandeja">
        <div class="row g-3 align-items-end">
            <div class="col-12 col-md-3 col-xl-2">
                <label for="<%= ddlPeriodo.ClientID %>" class="form-label">Período</label>
                <asp:DropDownList ID="ddlPeriodo" runat="server" CssClass="form-select gmail-filter-control">
                    <asp:ListItem Value="1">Hoy</asp:ListItem>
                    <asp:ListItem Value="7">Últimos 7 días</asp:ListItem>
                    <asp:ListItem Value="30" Selected="True">Últimos 30 días</asp:ListItem>
                    <asp:ListItem Value="custom">Personalizado</asp:ListItem>
                </asp:DropDownList>
            </div>
            <div class="col-12 col-md-3 col-xl-2">
                <label for="<%= ddlAgrupar.ClientID %>" class="form-label">Agrupar por</label>
                <asp:DropDownList ID="ddlAgrupar" runat="server" CssClass="form-select gmail-filter-control">
                    <asp:ListItem Value="date" Selected="True">Fecha</asp:ListItem>
                    <asp:ListItem Value="sender">Remitente</asp:ListItem>
                </asp:DropDownList>
            </div>
            <div class="col-6 col-md-3 col-xl-2">
                <label for="<%= txtDesde.ClientID %>" class="form-label">Desde</label>
                <asp:TextBox ID="txtDesde" runat="server" TextMode="Date" CssClass="form-control gmail-filter-control" />
            </div>
            <div class="col-6 col-md-3 col-xl-2">
                <label for="<%= txtHasta.ClientID %>" class="form-label">Hasta</label>
                <asp:TextBox ID="txtHasta" runat="server" TextMode="Date" CssClass="form-control gmail-filter-control" />
            </div>
            <div class="col-12 col-md-3 col-xl-2">
                <label for="<%= ddlEstado.ClientID %>" class="form-label">Estado</label>
                <asp:DropDownList ID="ddlEstado" runat="server" CssClass="form-select gmail-filter-control">
                    <asp:ListItem Value="">Todos</asp:ListItem>
                    <asp:ListItem Value="Procesado">Procesado</asp:ListItem>
                    <asp:ListItem Value="Con errores">Con errores</asp:ListItem>
                    <asp:ListItem Value="Descargado">Descargado</asp:ListItem>
                    <asp:ListItem Value="Pendiente">Pendiente</asp:ListItem>
                </asp:DropDownList>
            </div>
            <div class="col-12 col-md-6 col-xl-3">
                <label for="<%= txtRemitente.ClientID %>" class="form-label">Remitente</label>
                <asp:TextBox ID="txtRemitente" runat="server" CssClass="form-control gmail-filter-control" placeholder="Nombre o email" />
            </div>
            <div class="col-12 col-md-6 col-xl-3">
                <label for="<%= txtTexto.ClientID %>" class="form-label">Asunto</label>
                <asp:TextBox ID="txtTexto" runat="server" CssClass="form-control gmail-filter-control" placeholder="Buscar texto" />
            </div>
        </div>
        <div class="d-flex justify-content-between align-items-center flex-wrap gap-2 mt-3">
            <div class="text-secondary small">
                Los períodos rápidos actualizan automáticamente las fechas al aplicar.
            </div>
            <div class="d-flex gap-2">
                <asp:Button ID="btnLimpiarFiltros" runat="server" Text="Restablecer" CssClass="btn btn-outline-secondary" OnClick="LimpiarFiltros_Click" />
                <asp:Button ID="btnAplicarFiltros" runat="server" Text="Aplicar filtros" CssClass="btn btn-primary" OnClick="AplicarFiltros_Click" />
            </div>
        </div>
    </section>

    <p class="gmail-range-summary"><asp:Literal ID="litRangeSummary" runat="server" /></p>

    <asp:Panel ID="pnlSinMensajes" runat="server" CssClass="empty-state">
        <h2>No hay mensajes para los filtros seleccionados.</h2>
        <p class="text-secondary mb-0">Probá otro período o restablecé los filtros.</p>
    </asp:Panel>

    <asp:Panel ID="pnlTabla" runat="server" Visible="false">
        <asp:Repeater ID="rptDias" runat="server">
            <ItemTemplate>
                <section class="gmail-day-card">
                    <button type="button"
                            class="gmail-day-header"
                            data-bs-toggle="collapse"
                            data-bs-target="#<%# Eval("CollapseId") %>"
                            aria-expanded="<%# Eval("ExpandedAria") %>"
                            aria-controls="<%# Eval("CollapseId") %>">
                        <span class="gmail-day-title"><%#: Eval("Titulo") %></span>
                        <span class="gmail-day-stats">
                            <span><strong><%#: Eval("TotalMensajes") %></strong> mensajes</span>
                            <span><strong><%#: Eval("TotalAdjuntos") %></strong> adjuntos</span>
                            <span><strong><%#: Eval("Procesados") %></strong> procesados</span>
                            <span class='<%# Eval("ErroresCss") %>'><strong><%#: Eval("Errores") %></strong> errores</span>
                            <span class="gmail-day-chevron" aria-hidden="true">⌄</span>
                        </span>
                    </button>

                    <div id="<%# Eval("CollapseId") %>" class='collapse <%# Eval("CollapseCss") %>'>
                        <div class="table-responsive gmail-day-detail">
                            <table class="table table-hover align-middle mb-0 gmail-inbox-table">
                                <thead>
                                    <tr>
                                        <th class="col-time">Fecha / hora</th>
                                        <th class="col-sender">Remitente</th>
                                        <th class="col-subject">Asunto</th>
                                        <th class="col-attachments text-center">Adjuntos</th>
                                        <th class="col-status">Estado</th>
                                        <th class="col-action"></th>
                                    </tr>
                                </thead>
                                <tbody>
                                    <asp:Repeater ID="rptMensajesDia" runat="server" DataSource='<%# Eval("Mensajes") %>'>
                                        <ItemTemplate>
                                            <tr>
                                                <td><%#: Eval("FechaHoraTexto") %></td>
                                                <td class="text-truncate" title='<%#: Eval("Remitente") %>'><%#: Eval("Remitente") %></td>
                                                <td class="text-truncate" title='<%#: Eval("Asunto") %>'><%#: Eval("Asunto") %></td>
                                                <td class="text-center"><%#: Eval("CantidadAdjuntos") %></td>
                                                <td><span class='<%# "badge " + Eval("EstadoCss") %>'><%#: Eval("Estado") %></span></td>
                                                <td class="text-end"><a href='<%# "Gmail_Mensaje_Ver.aspx?id=" + Eval("Id") %>' class="text-decoration-none">Ver</a></td>
                                            </tr>
                                        </ItemTemplate>
                                    </asp:Repeater>
                                </tbody>
                            </table>
                        </div>
                    </div>
                </section>
            </ItemTemplate>
        </asp:Repeater>
    </asp:Panel>
</main>
</asp:Content>
