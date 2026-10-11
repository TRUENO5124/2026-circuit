# Third-party schematic symbols

The editor uses original SVG assets from these GitHub projects:

- `basverdoes/ElectricalSymbolLibrary`
  - Repository: https://github.com/basverdoes/ElectricalSymbolLibrary
  - Authors: Filip Dominec, Bas Verdoes
  - License: CC0 1.0 Universal (Public Domain Dedication)
  - Used for vectorized publication-quality schematic symbols covering ANSI and IEC standards (analog-iec/ and analog-ansi/ core symbols: resistors, capacitors, inductors, transformers, diodes, sources, grounds, and meters).
- `pacuserra/schemalib`
  - Repository: https://github.com/pacuserra/schemalib
  - License: MIT
  - Used for resistor, capacitor, inductor, transformer, diode, ground, sources, BJT, MOSFET and controlled-source symbols.
- `arcadie-cracan/circuitinsight-symbols`
  - Repository: https://github.com/arcadie-cracan/circuitinsight-symbols
  - License: CC BY 4.0
  - Used for the JFET symbol and the four-terminal NMOS/PMOS symbols.  The
    imported body arrows point outward for the P-substrate NMOS and inward for
    the N-well PMOS; the editor does not redraw or flip those arrows.

The files in this directory are kept as downloaded SVG assets. They retain the
upstream artwork and terminal-anchor metadata where provided; the editor adds
its own labels and connection handles around the imported artwork.
