<%@ Page Title="Inicio" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="Default.aspx.cs" Inherits="RecepcionDocumental._Default" %>
<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">
    <main>
        <header class="page-header"><p class="eyebrow">Panel general</p><h1>Recepción Documental</h1><p class="lead text-secondary">Seguimiento centralizado de mensajes y documentos recibidos.</p></header>
        <asp:Panel ID="pnlDatabaseWarning" runat="server" Visible="false" CssClass="alert alert-warning">La estructura inicial de la base todavía no está disponible. Ejecutá el script <strong>Database/001_EstructuraInicial.sql</strong>.</asp:Panel>
        <section class="row g-4" aria-label="Resumen">
            <div class="col-sm-6 col-xl-3"><article class="summary-card"><span>Cuenta Gmail</span><strong><asp:Literal ID="litCuentaGmail" runat="server" Text="0" /></strong><a href="Gmail_Config.aspx">Ver configuración</a></article></div>
            <div class="col-sm-6 col-xl-3"><article class="summary-card"><span>Mensajes recibidos</span><strong><asp:Literal ID="litMensajes" runat="server" Text="0" /></strong><a href="Gmail_Bandeja.aspx">Ver bandeja</a></article></div>
            <div class="col-sm-6 col-xl-3"><article class="summary-card"><span>Facturas</span><strong><asp:Literal ID="litFacturas" runat="server" Text="0" /></strong><a href="Documentos.aspx?clasificacion=FACTURA">Ver facturas</a></article></div>
            <div class="col-sm-6 col-xl-3" id="documentos"><article class="summary-card"><span>Para revisar</span><strong><asp:Literal ID="litRevisar" runat="server" Text="0" /></strong><a href="Documentos.aspx?clasificacion=REVISAR">Ver documentos</a></article></div>
        </section>
    </main>
</asp:Content>
