---
title: Solución de problemas
description: Agente no detectado, errores de conexión, impresoras y permisos.
---
| Síntoma | Causa probable | Solución |
|---|---|---|
| `agent_not_found` | Agente no instalado o detenido | Abra la app de bandeja (inicia el agente) o descárguelo |
| El navegador bloquea la conexión | Permiso de red local denegado | Permita «acceso a la red local» para el sitio en la configuración del navegador |
| `not_paired` | Autorización revocada o almacenamiento borrado | Vuelva a llamar a `pair()` |
| `permission_denied` | Se pidió un permiso no concedido (p. ej. RAW) | Empareje de nuevo con los permisos necesarios |
| `printer_not_found` | Nombre de impresora incorrecto o desconectada | Use los `id` de `printers.list()` |
| `unsupported_option` | El driver no declara esa opción (dúplex, papel) | Revise `capabilities` |
| Trabajo `unknown` | El agente se detuvo mientras enviaba al spooler | Verifique la impresora; no se reimprime solo |
| `completed` pero sin papel | `completed` = salió de la cola de Windows | Revise la impresora y la cola de Windows |
| SmartScreen advierte | Ejecutables aún sin firma | «Más información › Ejecutar de todas formas» tras verificar el SHA-256 |

**Diagnóstico**: app de bandeja › Configuración › *Exportar diagnóstico* (estado, impresoras, trabajos, auditoría y
registros; sin documentos ni credenciales).
