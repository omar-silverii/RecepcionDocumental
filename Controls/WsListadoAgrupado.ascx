<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="WsListadoAgrupado.ascx.cs" Inherits="RecepcionDocumental.Controls.WsListadoAgrupado" %>

<section class="ws-list-filter-card mb-3" aria-label="Filtros de listado">
    <div class="row g-3 align-items-end">
        <div class="col-12 col-md-3 col-xl-2">
            <label for="<%= ddlPeriodo.ClientID %>" class="form-label">Período</label>
            <asp:DropDownList ID="ddlPeriodo" runat="server" CssClass="form-select ws-list-filter-control">
                <asp:ListItem Value="1">Hoy</asp:ListItem>
                <asp:ListItem Value="7">Últimos 7 días</asp:ListItem>
                <asp:ListItem Value="30" Selected="True">Últimos 30 días</asp:ListItem>
                <asp:ListItem Value="custom">Personalizado</asp:ListItem>
            </asp:DropDownList>
        </div>
        <div class="col-12 col-md-3 col-xl-2">
            <label for="<%= ddlAgrupar.ClientID %>" class="form-label">Agrupar por</label>
            <asp:DropDownList ID="ddlAgrupar" runat="server" CssClass="form-select ws-list-filter-control">
                <asp:ListItem Value="date" Selected="True">Fecha</asp:ListItem>
                <asp:ListItem Value="sender">Remitente</asp:ListItem>
            </asp:DropDownList>
        </div>
        <div class="col-6 col-md-3 col-xl-2">
            <label for="<%= txtDesde.ClientID %>" class="form-label">Desde</label>
            <asp:TextBox ID="txtDesde" runat="server" TextMode="Date" CssClass="form-control ws-list-filter-control" />
        </div>
        <div class="col-6 col-md-3 col-xl-2">
            <label for="<%= txtHasta.ClientID %>" class="form-label">Hasta</label>
            <asp:TextBox ID="txtHasta" runat="server" TextMode="Date" CssClass="form-control ws-list-filter-control" />
        </div>
        <asp:Panel ID="pnlEstado" runat="server" CssClass="col-12 col-md-3 col-xl-2">
            <label for="<%= ddlEstado.ClientID %>" class="form-label"><asp:Literal ID="litEstadoLabel" runat="server" Text="Estado" /></label>
            <asp:DropDownList ID="ddlEstado" runat="server" CssClass="form-select ws-list-filter-control">
                <asp:ListItem Value="">Todos</asp:ListItem>
                <asp:ListItem Value="Procesado">Procesado</asp:ListItem>
                <asp:ListItem Value="Con errores">Con errores</asp:ListItem>
                <asp:ListItem Value="Descargado">Descargado</asp:ListItem>
                <asp:ListItem Value="Pendiente">Pendiente</asp:ListItem>
            </asp:DropDownList>
        </asp:Panel>
        <div class="col-12 col-md-6 col-xl-3">
            <label for="<%= txtRemitente.ClientID %>" class="form-label">Remitente</label>
            <asp:TextBox ID="txtRemitente" runat="server" CssClass="form-control ws-list-filter-control" placeholder="Nombre o email" />
        </div>
        <div class="col-12 col-md-6 col-xl-3">
            <label for="<%= txtTexto.ClientID %>" class="form-label"><asp:Literal ID="litTextoLabel" runat="server" Text="Asunto" /></label>
            <asp:TextBox ID="txtTexto" runat="server" CssClass="form-control ws-list-filter-control" placeholder="Buscar texto" />
        </div>
    </div>
    <div class="d-flex justify-content-between align-items-center flex-wrap gap-2 mt-3">
        <div class="text-secondary small">Los períodos rápidos actualizan automáticamente las fechas al aplicar.</div>
        <div class="d-flex gap-2">
            <asp:Button ID="btnLimpiarFiltros" runat="server" Text="Restablecer" CssClass="btn btn-outline-secondary" OnClick="LimpiarFiltros_Click" />
            <asp:Button ID="btnAplicarFiltros" runat="server" Text="Aplicar filtros" CssClass="btn btn-primary" OnClick="AplicarFiltros_Click" />
        </div>
    </div>
</section>

<asp:Panel ID="pnlFiltroError" runat="server" Visible="false" CssClass="alert alert-warning">
    <asp:Literal ID="litFiltroError" runat="server" />
</asp:Panel>

<p class="ws-list-range-summary"><asp:Literal ID="litRangeSummary" runat="server" /></p>

<asp:Panel ID="pnlVacio" runat="server" CssClass="empty-state">
    <h2><asp:Literal ID="litEmptyTitle" runat="server" /></h2>
    <p class="text-secondary mb-0"><asp:Literal ID="litEmptyText" runat="server" /></p>
</asp:Panel>

<asp:Panel ID="pnlTabla" runat="server" Visible="false">
    <asp:Repeater ID="rptGrupos" runat="server">
        <ItemTemplate>
            <section class="ws-list-group-card">
                <button type="button"
                        class="ws-list-group-header"
                        data-bs-toggle="collapse"
                        data-bs-target="#<%# Eval("CollapseId") %>"
                        aria-expanded="<%# Eval("ExpandedAria") %>"
                        aria-controls="<%# Eval("CollapseId") %>">
                    <span class="ws-list-group-title"><%#: Eval("Titulo") %></span>
                    <span class="ws-list-group-stats">
                        <asp:Repeater ID="rptStats" runat="server" DataSource='<%# Eval("Stats") %>'>
                            <ItemTemplate>
                                <span class='<%# Eval("CssClass") %>'><strong><%#: Eval("Valor") %></strong> <%#: Eval("Etiqueta") %></span>
                            </ItemTemplate>
                        </asp:Repeater>
                        <span class="ws-list-group-chevron" aria-hidden="true">⌄</span>
                    </span>
                </button>
                <div id="<%# Eval("CollapseId") %>" class='collapse <%# Eval("CollapseCss") %>'>
                    <div class="table-responsive ws-list-group-detail">
                        <table class='<%# "table table-hover align-middle mb-0 ws-list-table " + Eval("TableCssClass") %>'>
                            <colgroup>
                                <asp:Repeater ID="rptColGroup" runat="server" DataSource='<%# Eval("Columnas") %>'>
                                    <ItemTemplate><col class='<%# Eval("CssClass") %>' /></ItemTemplate>
                                </asp:Repeater>
                            </colgroup>
                            <thead>
                                <tr>
                                    <asp:Repeater ID="rptColumnas" runat="server" DataSource='<%# Eval("Columnas") %>'>
                                        <ItemTemplate><th class='<%# Eval("CssClass") %>'><%#: Eval("Titulo") %></th></ItemTemplate>
                                    </asp:Repeater>
                                </tr>
                            </thead>
                            <tbody>
                                <asp:Repeater ID="rptFilas" runat="server" DataSource='<%# Eval("Filas") %>'>
                                    <ItemTemplate>
                                        <tr>
                                            <asp:Repeater ID="rptCeldas" runat="server" DataSource='<%# Eval("Celdas") %>'>
                                                <ItemTemplate>
                                                    <td class='<%# Eval("CssClass") %>' title='<%#: Eval("Title") %>'>
                                                        <asp:Literal ID="litCell" runat="server" Mode="PassThrough" Text='<%# Eval("DisplayHtml") %>' />
                                                    </td>
                                                </ItemTemplate>
                                            </asp:Repeater>
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
