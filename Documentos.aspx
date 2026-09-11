<%@ Page Title="Documentos" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="Documentos.aspx.cs" Inherits="RecepcionDocumental.Documentos" %>
<%@ Register Src="~/Controls/WsListadoAgrupado.ascx" TagPrefix="ws" TagName="ListadoAgrupado" %>
<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">
<main class="documents-page">
    <div class="d-flex flex-column flex-sm-row align-items-sm-end justify-content-between gap-3 mb-4">
        <header class="page-header mb-0"><p class="eyebrow">Recepción</p><h1 class="mb-0">Documentos</h1></header>
        <div>
            <label class="form-label small fw-semibold text-secondary mb-1" for="<%= ddlClasificacion.ClientID %>">Clasificación</label>
            <asp:DropDownList ID="ddlClasificacion" runat="server" AutoPostBack="true" OnSelectedIndexChanged="Filtro_Changed" CssClass="form-select form-select-sm documents-filter">
                <asp:ListItem Value="">TODOS ACTIVOS</asp:ListItem>
                <asp:ListItem Value="FACTURA">FACTURAS</asp:ListItem>
                <asp:ListItem Value="REVISAR">PARA REVISAR</asp:ListItem>
                <asp:ListItem Value="DESCARTAR">DESCARTADOS</asp:ListItem>
            </asp:DropDownList>
        </div>
    </div>

    <asp:Panel ID="pnlError" runat="server" Visible="false" CssClass="alert alert-warning">No se pudo consultar documentos. Verificá la base y los scripts de Database.</asp:Panel>
    <asp:Panel ID="pnlResultado" runat="server" Visible="false" CssClass="alert alert-info"><asp:Literal ID="litResultado" runat="server" /></asp:Panel>
    <asp:Panel ID="pnlComenzarRevision" runat="server" Visible="false" CssClass="mb-3"><asp:HyperLink ID="lnkComenzarRevision" runat="server" CssClass="btn btn-primary">Comenzar revisión</asp:HyperLink></asp:Panel>
    <asp:HyperLink ID="lnkSample" runat="server" Visible="false" NavigateUrl="Documento_Revisar.aspx?muestra=1" CssClass="btn btn-outline-secondary mb-3">Revisión de control</asp:HyperLink>

    <ws:ListadoAgrupado ID="lstDocumentos" runat="server" />
</main>
</asp:Content>
