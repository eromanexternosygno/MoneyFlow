/* ============================================================================
   MoneyFlow - Script idempotente de base de datos
   ----------------------------------------------------------------------------
   Propósito:
     1) Crear la tabla  dbo.TransaccionesProcesadas  (si no existe).
     2) Agregar la columna  dbo.[User].PasswordHash  (si no existe).
     3) Asignar el hash de contraseña al usuario por defecto (nestor@gmail.com).

   Es idempotente: puede ejecutarse varias veces sin errores ni duplicados.

   Requisitos: SQL Server, autenticación con permisos de DDL/DML.
   ============================================================================ */

USE [MoneyFlowDb];   -- <-- Ajusta el nombre si tu base de datos se llama distinto
GO

/* ----------------------------------------------------------------------------
   1) Tabla dbo.TransaccionesProcesadas
   ---------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.TransaccionesProcesadas', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[TransaccionesProcesadas]
    (
        [Id]            INT            IDENTITY(1,1) NOT NULL,
        [IdEstacion]    INT            NOT NULL,
        [CR]            NVARCHAR(MAX)  NOT NULL,
        [LS]            NVARCHAR(MAX)  NOT NULL,
        [Nombre]        NVARCHAR(MAX)  NOT NULL,
        [FoliosTotales] NVARCHAR(MAX)  NOT NULL,
        [FechaCarga]    DATETIME       NOT NULL
            CONSTRAINT [DF_TransaccionesProcesadas_FechaCarga] DEFAULT (GETDATE()),

        CONSTRAINT [PK_TransaccionesProcesadas]
            PRIMARY KEY CLUSTERED ([Id] ASC)
    );

    PRINT 'Tabla [dbo].[TransaccionesProcesadas] creada correctamente.';
END
ELSE
BEGIN
    PRINT 'La tabla [dbo].[TransaccionesProcesadas] ya existe; se omite su creacion.';
END
GO

/* ----------------------------------------------------------------------------
   2) Columna dbo.[User].PasswordHash
   ---------------------------------------------------------------------------- */
IF COL_LENGTH(N'dbo.[User]', N'PasswordHash') IS NULL
BEGIN
    ALTER TABLE [dbo].[User]
        ADD [PasswordHash] NVARCHAR(MAX) NOT NULL
            CONSTRAINT [DF_User_PasswordHash] DEFAULT (N'');

    PRINT 'Columna [dbo].[User].[PasswordHash] agregada correctamente.';
END
ELSE
BEGIN
    PRINT 'La columna [dbo].[User].[PasswordHash] ya existe; se omite.';
END
GO

/* ----------------------------------------------------------------------------
   3) Hash de contrasena para el usuario por defecto (nestor@gmail.com)
      Hash estatico de "password123" (mismo valor que usa el seed de EF Core).
   ---------------------------------------------------------------------------- */
DECLARE @Hash NVARCHAR(MAX)
    = N'AQAAAAEAACcQAAAAEI3E/O7ptZvXiEJtpztwUuGTUxOmSnN63pcdBOKI1/rP55eKbRF7yzV93fRH8I4GHw==';

UPDATE [dbo].[User]
SET    [PasswordHash] = @Hash
WHERE  [UserId] = 1
  AND  ISNULL([PasswordHash], N'') <> @Hash;

IF @@ROWCOUNT > 0
    PRINT 'Hash del usuario por defecto actualizado.';
ELSE
    PRINT 'El usuario por defecto ya tiene el hash correcto (no hubo cambios).';
GO

PRINT 'Script finalizado correctamente.';
GO
