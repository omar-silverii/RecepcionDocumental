<%@ Page Title="Autorización Gmail" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" Async="true" CodeBehind="Gmail_OAuthCallback.aspx.cs" Inherits="RecepcionDocumental.Gmail_OAuthCallback" %>
<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">
<main><header class="page-header"><p class="eyebrow">Gmail</p><h1>Autorización de cuenta</h1></header>
<asp:Panel ID="pnlResultado" runat="server" CssClass="alert alert-info"><asp:Literal ID="litResultado" runat="server" Text="Procesando la autorización…" /></asp:Panel>
<p><a href="Gmail_Config.aspx">Volver a configuración</a></p></main>
</asp:Content>
