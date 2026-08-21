<%@ Page Title="Documentos" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="Documentos.aspx.cs" Inherits="RecepcionDocumental.Documentos" %>
<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server"><main class="documents-page">
<div class="d-flex flex-column flex-sm-row align-items-sm-end justify-content-between gap-3 mb-4">
    <header class="page-header mb-0"><p class="eyebrow">Recepción</p><h1 class="mb-0">Documentos</h1></header>
    <div>
        <label class="form-label small fw-semibold text-secondary mb-1" for="<%= ddlClasificacion.ClientID %>">Clasificación</label>
        <asp:DropDownList ID="ddlClasificacion" runat="server" AutoPostBack="true" OnSelectedIndexChanged="Filtro_Changed" CssClass="form-select form-select-sm documents-filter"><asp:ListItem Value="">TODOS</asp:ListItem><asp:ListItem Value="FACTURA">FACTURA</asp:ListItem><asp:ListItem Value="REVISAR">REVISAR</asp:ListItem></asp:DropDownList>
    </div>
</div>
<asp:Panel ID="pnlError" runat="server" Visible="false" CssClass="alert alert-warning">No se pudo consultar DocumentoRecepcion. Ejecutá Database/005_SelectorFacturaBase.sql.</asp:Panel>
<asp:Panel ID="pnlVacio" runat="server" Visible="false" CssClass="empty-state">No hay documentos para el filtro seleccionado.</asp:Panel>
<asp:Panel ID="pnlTabla" runat="server" CssClass="card border-0 shadow-sm overflow-hidden documents-table-card">
<div class="table-responsive documents-table-wrap">
    <asp:Repeater ID="rptDocumentos" runat="server">
        <HeaderTemplate>
            <table class="table table-striped table-hover table-sm align-middle mb-0 documents-table">
                <colgroup>
                    <col class="col-date" /><col class="col-sender" /><col class="col-subject" />
                    <col class="col-name" /><col class="col-classification" /><col class="col-method" />
                    <col class="col-confidence" /><col class="col-reason" /><col class="col-origin" />
                </colgroup>
                <thead><tr><th>Fecha</th><th>Remitente</th><th>Asunto</th><th>Nombre documento</th><th>Clasificación</th><th>Método</th><th>Confianza</th><th>Motivo</th><th>Origen</th></tr></thead>
                <tbody>
        </HeaderTemplate>
        <ItemTemplate>
            <tr>
                <td class="doc-date text-nowrap"><%#: Eval("Fecha", "{0:dd/MM/yyyy HH:mm}") %></td>
                <td class="doc-sender text-truncate" title='<%#: Eval("Remitente") %>'><%#: Eval("Remitente") %></td>
                <td class="doc-subject text-truncate" title='<%#: Eval("Asunto") %>'><%#: Eval("Asunto") %></td>
                <td class="doc-name text-truncate" title='<%#: Eval("NombreOriginal") %>'><%#: Eval("NombreOriginal") %></td>
                <td class="doc-classification"><span class="badge rounded-pill text-bg-secondary"><%#: Eval("Clasificacion") %></span></td>
                <td class="doc-method"><span class="badge bg-light text-dark border fw-normal"><%#: Eval("MetodoDeteccion") %></span></td>
                <td class="doc-confidence"><%#: Eval("Confianza") %></td>
                <td class="doc-reason text-truncate" title='<%#: Eval("Motivo") %>'><%#: Eval("Motivo") %></td>
                <td class="doc-origin"><%#: Eval("OrigenTipo") %></td>
            </tr>
        </ItemTemplate>
        <FooterTemplate></tbody></table></FooterTemplate>
    </asp:Repeater>
</div>
</asp:Panel>
</main></asp:Content>
