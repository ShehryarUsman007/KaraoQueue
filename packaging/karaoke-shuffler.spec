# RPM package for Karaoke Shuffler.
# Repackages the already-published self-contained build (no compiling here).
# Build:  rpmbuild -bb --define "_version 1.0.0" --define "_appdir <published linux-x64 folder>" --define "_pkgdir <packaging folder>" karaoke-shuffler.spec

Name:           karaoke-shuffler
Version:        %{_version}
Release:        1
Summary:        Shuffle and track the karaoke singer rotation
License:        Apache-2.0
URL:            https://github.com/ShehryarUsman007/Karaoke-Shuffler
ExclusiveArch:  x86_64

# .NET is bundled; these are the system libraries the app's UI needs
Requires:       libX11 libXext libXfixes libXi libXrandr fontconfig freetype libicu xdg-utils

# prebuilt .NET files: don't strip them, scan them for dependencies or make debug packages
AutoReqProv:    no
%global debug_package %{nil}
%global __os_install_post %{nil}

%description
A very basic karaoke singer rotation app, made for a karaoke group in Windsor, Ontario.
Shuffles the singers, highlights whose turn it is and handles late arrivals.

%install
mkdir -p %{buildroot}/opt/karaoke-shuffler %{buildroot}%{_bindir}
cp -a %{_appdir}/. %{buildroot}/opt/karaoke-shuffler/
ln -s /opt/karaoke-shuffler/Singer_Selector %{buildroot}%{_bindir}/karaoke-shuffler
install -Dm644 %{_pkgdir}/karaoke-shuffler.desktop %{buildroot}%{_datadir}/applications/karaoke-shuffler.desktop
install -Dm644 %{_pkgdir}/icon.png %{buildroot}%{_datadir}/icons/hicolor/256x256/apps/karaoke-shuffler.png
install -Dm644 %{_pkgdir}/icon.svg %{buildroot}%{_datadir}/icons/hicolor/scalable/apps/karaoke-shuffler.svg

%files
/opt/karaoke-shuffler
%{_bindir}/karaoke-shuffler
%{_datadir}/applications/karaoke-shuffler.desktop
%{_datadir}/icons/hicolor/256x256/apps/karaoke-shuffler.png
%{_datadir}/icons/hicolor/scalable/apps/karaoke-shuffler.svg
