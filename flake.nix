{
  description = "Markdown Memory Notes development shell";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
  };

  outputs = { self, nixpkgs }:
    let
      systems = [ "x86_64-linux" "aarch64-linux" ];
      forAllSystems = nixpkgs.lib.genAttrs systems;
    in
    {
      devShells = forAllSystems (system:
        let
          pkgs = import nixpkgs { inherit system; };
          dotnetSdk = pkgs.dotnetCorePackages.sdk_10_0;
        in
        {
          default = pkgs.mkShell {
            packages = [
              dotnetSdk
              pkgs.git
              pkgs.sqlite
              pkgs.fontconfig
              pkgs.libICE
              pkgs.libSM
              pkgs.libxkbcommon
              pkgs.libx11
              pkgs.libxi
            ];

            DOTNET_CLI_TELEMETRY_OPTOUT = "1";
            DOTNET_NOLOGO = "1";

            shellHook = ''
              export DOTNET_ROOT=${dotnetSdk}
              export PATH="$DOTNET_ROOT/bin:$PATH"
              export LD_LIBRARY_PATH="publish/linux-x64:${pkgs.fontconfig.lib}/lib:${pkgs.libICE}/lib:${pkgs.libSM}/lib:${pkgs.libxkbcommon}/lib:${pkgs.libx11}/lib:${pkgs.libxi}/lib''${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
              echo "Markdown Memory Notes dev shell"
              dotnet --version
            '';
          };
        });
    };
}
