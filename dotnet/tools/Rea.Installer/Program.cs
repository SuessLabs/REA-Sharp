using Rea.Installer;

using var payload = typeof(Installer).Assembly.GetManifestResourceStream(PortableInstaller.ResourceName);
return payload is null
    ? await Installer.RunAsync(args, Console.In, Console.Out, Console.Error)
    : await PortableInstaller.RunAsync(payload, args, Console.In, Console.Out, Console.Error);
