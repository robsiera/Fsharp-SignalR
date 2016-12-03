namespace StockTicker

open Owin
open Microsoft.Owin
open System
open System.Collections.Generic
open System.IO
open System.Net.Http
open System.Web
open System.Web.Http
open System.Web.Mvc
open System.Web.Routing
open System.Web.Http
open System.Web.Http.Owin
open System.Threading
open StockTicker.Controllers
open Microsoft.AspNet.SignalR
open Microsoft.AspNet.SignalR.Messaging
open Microsoft.AspNet.SignalR.Hubs
open StockTicker.FileSystemBusModule
open System.Web.Http.Dispatcher
open System.Web.Http.Controllers
open System.Reactive
open FSharp.Reactive
open StockTicker.Commands
open CommandHandler
open StockTickerServer


// Transform the web api controller in an Observable publisher,
// register the controller to the command dispatcher.

// Using Pub/Sub API of Reactive Extensions used to pipe messages to an Agent
// The Agent only dependd on IObserver implementation reducing the dependencies 

// Hook into the Web API framework where it creates controllers
type CompositionRoot(tradingRequestObserver:IObserver<CommandWrapper>) =
    interface IHttpControllerActivator with
        member this.Create(request, controllerDescriptor, controllerType) =
            if controllerType = typeof<TradingController> then
                let c = new TradingController()
                c   // c.Subscribe  agent.Post
                |> Observable.subscribeObserver tradingRequestObserver                
                |> request.RegisterForDispose
                c :> IHttpController
            else
                raise
                <| ArgumentException(
                    sprintf "Unknown controller type requested: %O" controllerType,
                    "controllerType")

(*  Startup Class
    The server needs to know which URL to intercept and direct to SignalR. To do that, 
    I add an OWIN startup class. 
 *)

/// Route for ASP.NET Web API applications
type HttpRoute = {
    controller : string
    id : RouteParameter }
    
type ErrorHandlingPipelineModule() =
    inherit HubPipelineModule()

    override x.OnIncomingError(exceptionContext:ExceptionContext, invokerContext:IHubIncomingInvokerContext) =
            System.Diagnostics.Debug.WriteLine("=> Exception " + exceptionContext.Error.Message)
            if exceptionContext.Error.InnerException <> null then
                System.Diagnostics.Debug.WriteLine("=> Inner Exception " + exceptionContext.Error.InnerException.Message)
            base.OnIncomingError(exceptionContext, invokerContext)


[<Sealed>]
type Startup() = 

        // Controller subscriber in for of agent 
        // dispatch eterogenous message depending on the incoming
        // Only message at the time 
    let agent = new Agent<CommandWrapper>(fun inbox ->
            let rec loop () =
                async {
                    let! cmd = inbox.Receive()                 
                    do! cmd |> AsyncHandle
                    return! loop() }
            loop())
    do agent.Start()


    // Additional Web API settings
    member __.Configuration(builder : IAppBuilder) = 

        let config =
            let config = new HttpConfiguration()
            // Configure routing
            config.MapHttpAttributeRoutes()
            
            // Configure serialization
            config.Formatters.XmlFormatter.UseXmlSerializer <- true
            config.Formatters.JsonFormatter.SerializerSettings.ContractResolver 
                            <- Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()

            config.Routes.MapHttpRoute("tradingApi", "api/trading/{id}", 
                 { controller = "Trading"; id = RouteParameter.Optional }) |> ignore


            config.Routes.MapHttpRoute("DefaultApi", "api/{controller}/{id}", 
                 { controller = "{controller}"; id = RouteParameter.Optional }) |> ignore

            // replace the defaulr controller activator 
            config.Services.Replace(typeof<IHttpControllerActivator>,
                                    // I create a subscription controller to the Agent
                                    // Each time a message come in (post) the publisher send the message (OnNext)
                                    // to all the subscribers, in this case the Agent 
                                    CompositionRoot( Observer.Create(fun x -> agent.Post(x)) ))

            config
       
        let configSignalR = new HubConfiguration(EnableDetailedErrors = true)

        GlobalHost.HubPipeline.AddModule(new ErrorHandlingPipelineModule()) |> ignore

//      To inform SignalR that it is the message bus to be used, we would just need to execute the following code during startup:
//        GlobalHost.DependencyResolver.Register(typeof<IMessageBus>, fun () -> 
//             FileSystemBus.Create(GlobalHost.DependencyResolver,
//                                  ScaleoutConfiguration()).Value :> Object)

        Owin.CorsExtensions.UseCors(builder, Microsoft.Owin.Cors.CorsOptions.AllowAll) |> ignore
        
        //  MapSignalR() is an extension method of IAppBuilder provided by SignalR to facilitate mapping 
        //  and configuration of the hub service.
        //  The generic overload MapSignalR<TConnection> is used to map persistent connections, that we do not have to specify the classes that implement the services
        builder.MapSignalR(configSignalR) |> ignore
        
        //config.MessageHandlers.Add(new MessageLoggingHandler())
        
        builder.UseWebApi(config) |> ignore


[<assembly: OwinStartupAttribute(typeof<Startup>)>]
do ()