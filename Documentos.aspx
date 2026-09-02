<%@ Page Title="Documentos" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="Documentos.aspx.cs" Inherits="RecepcionDocumental.Documentos" %>
<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server"><main class="documents-page">
<div class="d-flex flex-column flex-sm-row align-items-sm-end justify-content-between gap-3 mb-4">
    <header class="page-header mb-0"><p class="eyebrow">Recepción</p><h1 class="mb-0">Documentos</h1></header>
    <div>
        <label class="form-label small fw-semibold text-secondary mb-1" for="<%= ddlClasificacion.ClientID %>">Clasificación</label>
        <asp:DropDownList ID="ddlClasificacion" runat="server" AutoPostBack="true" OnSelectedIndexChanged="Filtro_Changed" CssClass="form-select form-select-sm documents-filter"><asp:ListItem Value="">TODOS ACTIVOS</asp:ListItem><asp:ListItem Value="FACTURA">FACTURAS</asp:ListItem><asp:ListItem Value="REVISAR">PARA REVISAR</asp:ListItem><asp:ListItem Value="DESCARTAR">DESCARTADOS MANUALMENTE</asp:ListItem></asp:DropDownList>
    </div>
</div>
<asp:Panel ID="pnlError" runat="server" Visible="false" CssClass="alert alert-warning">No se pudo consultar DocumentoRecepcion. Ejecutá Database/005_SelectorFacturaBase.sql.</asp:Panel>
<asp:Panel ID="pnlVacio" runat="server" Visible="false" CssClass="empty-state">No hay documentos para el filtro seleccionado.</asp:Panel>
<asp:Panel ID="pnlResultado" runat="server" Visible="false" CssClass="alert alert-info"><asp:Literal ID="litResultado" runat="server" /></asp:Panel>
<asp:Panel ID="pnlComenzarRevision" runat="server" Visible="false" CssClass="mb-3"><asp:HyperLink ID="lnkComenzarRevision" runat="server" CssClass="btn btn-primary">Comenzar revisión</asp:HyperLink></asp:Panel>
<asp:HyperLink ID="lnkSample" runat="server" Visible="false" NavigateUrl="Documento_Revisar.aspx?muestra=1" CssClass="btn btn-outline-secondary mb-3">Revisión de control</asp:HyperLink>
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
                <thead><tr><th>Fecha</th><th>Remitente</th><th>Asunto</th><th>Nombre documento</th><th>Clasificación automática</th><th>Resultado humano</th><th>Método</th><th>Confianza</th><th>Motivo</th><th>Origen</th><th>Acciones</th></tr></thead>
                <tbody>
        </HeaderTemplate>
        <ItemTemplate>
            <tr>
                <td class="doc-date text-nowrap"><%#: Eval("Fecha", "{0:dd/MM/yyyy HH:mm}") %></td>
                <td class="doc-sender text-truncate" title='<%#: Eval("Remitente") %>'><%#: Eval("Remitente") %></td>
                <td class="doc-subject text-truncate" title='<%#: Eval("Asunto") %>'><%#: Eval("Asunto") %></td>
                <td class="doc-name text-truncate" title='<%#: Eval("NombreOriginal") %>'><%#: Eval("NombreOriginal") %></td>
                <td class="doc-classification"><span class="badge rounded-pill text-bg-secondary"><%#: Eval("Clasificacion") %></span></td>
                <td><%#: Eval("ResultadoRevision") ?? "Pendiente" %><%# Eval("EtiquetaRevision") == null ? "" : " — "+Eval("EtiquetaRevision") %><%# Eval("ResultadoRevision") as string == "FACTURA" ? " — CONFIRMADA MANUALMENTE" : "" %></td>
                <td class="doc-method"><span class="badge bg-light text-dark border fw-normal"><%#: Eval("MetodoDeteccion") %></span></td>
                <td class="doc-confidence"><%#: Eval("Confianza") %></td>
                <td class="doc-reason text-truncate" title='<%#: Eval("Motivo") %>'><%#: Eval("Motivo") %></td>
                <td class="doc-origin"><%#: Eval("OrigenTipo") %></td>
                <td class="text-nowrap"><asp:PlaceHolder runat="server" Visible='<%# (bool)Eval("PendienteRevision") %>'><a class="btn btn-sm btn-primary me-1" href='<%#: "Documento_Revisar.aspx?id="+Eval("Id") %>'>Revisar</a></asp:PlaceHolder><a class="btn btn-sm btn-outline-secondary" target="_blank" href='<%#: "Documento_Ver.aspx?id="+Eval("Id") %>'>Ver</a></td>
            </tr>
        </ItemTemplate>
        <FooterTemplate></tbody></table></FooterTemplate>
    </asp:Repeater>
</div>
</asp:Panel>
</main></asp:Content>
