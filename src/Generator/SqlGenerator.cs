using System;
using System.Data;
using System.IO;
using System.Collections;
using System.Text;

using Spring2.DataTierGenerator;
using Spring2.DataTierGenerator.Element;
using Spring2.DataTierGenerator.Util;

using Constraint = Spring2.DataTierGenerator.Element.Constraint;

namespace Spring2.DataTierGenerator.Generator {
    /// <summary>
    /// Generates stored procedures and associated data access code for the specified database.
    /// </summary>
    internal class SqlGenerator : GeneratorSkeleton, IGenerator {

	// sql keywords that need escaping.  all values need to be enclosed in a pair of | characters.  values should be lower case.
	private static readonly String KEYWORDS = 
		"|add|except|percent|all|exec|plan|alter|execute|precision|and|exists|primary|any|exit|print|as|fetch" +
		"|proc|asc|file|procedure|authorization|fillfactor|public|backup|for|raiserror|begin|foreign" +
		"|read|between|freetext|readtext|break|freetexttable|reconfigure|browse|from|references|bulk" +
		"|full|replication|by|function|restore|cascade|goto|restrict|case|grant|return|check|group" +
		"|revoke|checkpoint|having|right|close|holdlock|rollback|clustered|identity|rowcount|coalesce" +
		"|identity_insert|rowguidcol|collate|identitycol|rule|column|if|save|commit|in|schema|compute" +
		"|index|select|constraint|inner|session_user|contains|insert|set|containstable|intersect" +
		"|setuser|continue|into|shutdown|convert|is|some|create|join|statistics|cross|key|system_user" +
		"|current|kill|table|current_date|left|textsize|current_time|like|then|current_timestamp" +
		"|lineno|to|current_user|load|top|cursor|national||tran|database|nocheck|transaction|dbcc" +
		"|nonclustered|trigger|deallocate|not|truncate|declare|null|tsequal|default|nullif|union" +
		"|delete|of|unique|deny|off|update|desc|offsets|updatetext|disk|on|use|distinct|open|user" +
		"|distributed|opendatasource|values|double|openquery|varying|drop|openrowset|view|dummy" +
		"|openxml|waitfor|dump|option|when|else|or|where|end|order|while|errlvl|outer|with|escape|over|writetext|";

	private SqlEntity sqlentity;

	/// <summary>
	/// Contructor for the Generator class.
	/// </summary>
	/// <param name="strConnectionString">Connecion string to a SQL Server database.</param>
	public SqlGenerator(Configuration options, SqlEntity sqlentity) : base(options) {
	    this.sqlentity = sqlentity;
//	    if (options.SingleFile) {
//		String fileName = options.SqlScriptDirectory + "\\" + options.Database + ".sql";
//		if (File.Exists(fileName))
//		    File.Delete(fileName);
//	    }
	}

	public override void Generate() {
	    if (sqlentity.GenerateSqlTableScripts) CreateTable();
	    if (sqlentity.GenerateSqlViewScripts) CreateView();
	    if (sqlentity.GenerateInsertStoredProcScript) CreateInsertStoredProcedure();
	    if (sqlentity.GenerateUpdateStoredProcScript && sqlentity.HasUpdatableColumns()) CreateUpdateStoredProcedure();
	    if (sqlentity.GenerateDeleteStoredProcScript) CreateDeleteStoredProcedures();
	    //if (sqlentity.GenerateSelectStoredProcScript) CreateSelectStoredProcedures();
	}
		
	/// <summary>
	/// Creates an insert stored procedure SQL script for the specified table
	/// </summary>
	/// <param name="table">Name of the table the stored procedure should be generated from.</param>
	/// <param name="fields">ArrayList object containing one or more Field objects as defined in the table.</param>
	private void CreateInsertStoredProcedure() {
	    StringBuilder sb = new StringBuilder();
	    String strProcName = options.GetProcName(sqlentity.Name, "Insert");
	
	    if (sqlentity.ScriptForIndexedViews) {
		sb.Append("SET ANSI_NULLS ON\n");
		sb.Append("SET QUOTED_IDENTIFIER ON\n");
		sb.Append("GO\n\n");
	    }
				
	    sb.Append("CREATE PROCEDURE " + strProcName + "\n");
	
	    // Create the parameter list
	    Boolean first = true;
	    foreach (Column column in sqlentity.Columns) {
		if (!column.ViewColumn) {
		    if (!column.Identity) {
			if (!first) {
			    sb.Append(",\n");
			} else {
			    first=false;
			}
			sb.Append("\t").Append(column.SqlParameter);
		    }
		}
	    }
	    sb.Append("\n\nAS\n\n");

	    foreach (Column column in sqlentity.Columns) {
		if (column.RowGuidCol) {
		    sb.Append("SET @" + column.Name + " = @@NEWID()\n\n");
		    break;
		}
	    }
				
	    sb.Append("INSERT INTO " + EscapeSqlName(sqlentity.Name) + " (\n");
				
	    // Create the parameter list
	    first = true;
	    foreach (Column column in sqlentity.Columns) {
		// Is the current field an identity column?
		if (!column.ViewColumn) {
		    if (!column.Identity) {
			if (!first) {
			    sb.Append(",\n");
			} else {
			    first=false;
			}
			sb.Append("\t" + EscapeSqlName(column.Name) );
		    }
		}
	    }
	    sb.Append(")\nVALUES (\n");
	
	    // Create the parameter list
	    first=true;
	    foreach (Column column in sqlentity.Columns) {
		if (!column.ViewColumn) {
		    if (!column.Identity) {
			if (!first) {
			    sb.Append(",\n");
			} else {
			    first=false;
			}
			sb.Append("\t@" + column.Name);
		    }
		}
	    }
	    sb.Append(")\n");
	
	    sb.Append("\n\nif @@rowcount <> 1 or @@error!=0\n");
	    sb.Append("    BEGIN\n");
	    sb.Append("        RAISERROR  20000 '").Append(strProcName).Append(": Unable to insert new row into ").Append(sqlentity.Name).Append(" table '\n");
	    sb.Append("        RETURN(-1)\n");
	    sb.Append("    END\n\n");
	
	    sb.Append("return @@IDENTITY\n");
	    sb.Append("GO\n\n");

	    if (sqlentity.ScriptForIndexedViews) {
		sb.Append("SET ANSI_NULLS ON\n");
		sb.Append("SET QUOTED_IDENTIFIER ON\n");
		sb.Append("GO\n\n");
	    }
	    
	    // Write out the stored procedure
	    WriteProcToFile(strProcName, sb.ToString());
	    sb = null;
	}
	
	/// <summary>
	/// Creates an update stored procedure SQL script for the specified table
	/// </summary>
	/// <param name="table">Name of the table the stored procedure should be generated from.</param>
	/// <param name="fields">ArrayList object containing one or more Field objects as defined in the table.</param>
	private void CreateUpdateStoredProcedure() {
	    StringBuilder sb = new StringBuilder();

	    // NOTE: this must be done before creating the stored proc to be effective
	    if (sqlentity.ScriptForIndexedViews) {
		sb.Append("SET ANSI_NULLS ON\n");
		sb.Append("SET QUOTED_IDENTIFIER ON\n");
		sb.Append("GO\n\n");
	    }

	    sb.Append("CREATE PROCEDURE " + options.GetProcName(sqlentity.Name, "Update") + "\n\n");
	
	    // Create the parameter list
	    Boolean first = true;
	    foreach (Column column in sqlentity.Columns) {
		if (!column.ViewColumn) {
		    if (!first) {
			sb.Append(",\n");
		    } else {
			first=false;
		    }
		    sb.Append("\t").Append(column.SqlParameter);
		}
	    }
	    sb.Append("\n\nAS\n\n");

	    sb.Append("\nUPDATE\n\t" + EscapeSqlName(sqlentity.Name) + "\n");
	    sb.Append("SET\n");

	    // Create the set statement
	    first = true;
	    foreach (Column column in sqlentity.Columns) {
		if (!column.ViewColumn) {
		    if (!column.Identity && !column.RowGuidCol) {
			if (!sqlentity.IsPrimaryKeyColumn(column.Name)) {
			    if (!first) {
				sb.Append(",\n");
			    } else {
				first=false;
			    }
			    sb.Append("\t" + EscapeSqlName(column.Name) + " = @" + EscapeSqlName(column.Name));
			}
		    }
		}
	    }
	    sb.Append("\n");
	    sb.Append("WHERE\n");
				
	    // Create the where clause
	    first = true;
	    foreach (Column column in sqlentity.Columns) {
		if (!column.ViewColumn) {
		    if (column.Identity || column.RowGuidCol || sqlentity.IsPrimaryKeyColumn(column.Name)) {
			if (!first) {
			    sb.Append("\t AND ");
			} else {
			    first=false;
			}
			sb.Append("" + EscapeSqlName(column.Name) + " = @" + EscapeSqlName(column.Name) + "\n");
		    }
		}
	    }
	
	    sb.Append("\n\nif @@ROWCOUNT <> 1\n");
	    sb.Append("    BEGIN\n");
	    sb.Append("        RAISERROR  ('").Append(options.GetProcName(sqlentity.Name, "Update")).Append(":  update was expected to update a single row and updated %d rows', 16,1, @@ROWCOUNT)\n");
	    sb.Append("        RETURN(-1)\n");
	    sb.Append("    END\n");

	    sb.Append("GO\n\n");

	    if (sqlentity.ScriptForIndexedViews) {
		sb.Append("SET ANSI_NULLS ON\n");
		sb.Append("SET QUOTED_IDENTIFIER ON\n");
		sb.Append("GO\n\n");
	    }
	
	    // Write out the stored procedure
	    WriteProcToFile(options.GetProcName(sqlentity.Name, "Update"), sb.ToString());
	    sb = null;
	}
	
	
	private void CreateDeleteStoredProcedures() {
	    String strProcName = options.GetProcName(sqlentity.Name, "Delete");
	    StringBuilder sb = new StringBuilder();

	    if (sqlentity.ScriptForIndexedViews) {
		sb.Append("SET ANSI_NULLS ON\n");
		sb.Append("SET QUOTED_IDENTIFIER ON\n");
		sb.Append("GO\n\n");
	    }
	
	    sb.Append("CREATE PROCEDURE " + strProcName + "\n\n");				
					
	    // Create the parameter list
	    Boolean first = true;
	    foreach (Column column in sqlentity.GetPrimaryKeyColumns()) {
		if (!first) {
		    sb.Append(",\n");
		} else {
		    first = false;
		}
		sb.Append("@" + column.Name + "\t" + column.SqlType.Declaration);
	    }
					
	    sb.Append("\n\nAS\n\n");

	    // Create the where clause
	    StringBuilder where = new StringBuilder();
	    first = true;
	    foreach (Column column in sqlentity.GetPrimaryKeyColumns()) {
		if (!first) {
		    where.Append(" and\n");
		} else {
		    first = false;
		}
		where.Append("\t" + EscapeSqlName(column.Name) + " = @" + EscapeSqlName(column.Name) + "\n");
	    }
	
	    sb.Append("if not exists(SELECT ").Append("*").Append(" FROM ").Append(EscapeSqlName(sqlentity.Name)).Append(" WHERE (").Append(where.ToString()).Append("))\n");
	    sb.Append("    BEGIN\n");
	    sb.Append("        RAISERROR  ('").Append(strProcName).Append(": ").Append("record").Append(" not found to delete', 16,1)\n");
	    sb.Append("        RETURN(-1)\n");
	    sb.Append("    END\n\n");
	
	
	    sb.Append("DELETE\n");
	    sb.Append("FROM\n\t" + EscapeSqlName(sqlentity.Name) + "\n");
	    sb.Append("WHERE \n");
	
	    sb.Append(where.ToString());
	    sb.Append("GO\n\n");
	    
	    if (sqlentity.ScriptForIndexedViews) {
		sb.Append("SET ANSI_NULLS ON\n");
		sb.Append("SET QUOTED_IDENTIFIER ON\n");
		sb.Append("GO\n\n");
	    }
					
	    // Write out the stored procedure
	    WriteProcToFile(strProcName, sb.ToString());
	    sb = null;
	}
	
	
	//	/// <summary>
	//	/// Creates a select stored procedure SQL script for the specified table by the table's identity or uniqueidentifier column,
	//	///	and select stored procedures for all foreign keys columns in the table.
	//	/// </summary>
	//	/// <param name="table">Name of the table the stored procedure should be generated from.</param>
	//	/// <param name="fields">ArrayList object containing one or more Field objects as defined in the table.</param>
	//	public void CreateSelectStoredProcedures() {
	//	    ArrayList		arrKeyList;
	//	    Field		field;
	//	    string			strColumnName;
	//	    int				i;
	//	    StringBuilder	sb;
	//	    string			strPrimaryKeyList;
	//
	//	    /************************************************************************************/
	//	    // Create the full list stored procedure
	//	    String			strProcName;
	//			
	//	    // Create the SQL for the stored procedure
	//	    sb = new StringBuilder(1024);
	//
	//	    strProcName = options.GetProcName(entity.SqlObject, "Select");
	//
	//	    sb.Append("CREATE PROCEDURE " + strProcName + "\n\n");
	//	    sb.Append("\n\nAS\n\n");
	//	    sb.Append("SELECT\n\t*\n");
	//	    sb.Append("FROM\n\t[");
	//	    if (options.UseViews)
	//		sb.Append("vw");
	//	    sb.Append(entity.SqlObject);
	//	    sb.Append("]\n");
	//			
	//	    // Write out the stored procedure
	//	    WriteProcToFile(strProcName, sb.ToString() + "\nGO\n\n");
	//	    sb = null;
	//
	//	    // Create the array list of key fields
	//	    strPrimaryKeyList = "";
	//	    arrKeyList = new ArrayList();
	//	    for (i = 0; i < entity.Fields.Count; i++) {
	//		field = (Field)entity.Fields[i];
	//		if (field.IsPrimaryKey) {
	//		    arrKeyList.Add(field);
	//		    strPrimaryKeyList += field.SqlName.Replace(" ", "_") + "_";
	//		}
	//		field = null;
	//	    }
	//			
	//	    // Trim off the last underscore
	//	    if (strPrimaryKeyList.Length > 0)
	//		strPrimaryKeyList = strPrimaryKeyList.Substring(0, strPrimaryKeyList.Length - 1);
	//
	//	    /************************************************************************************/
	//	    // Create the stored procedures, with parameters for each identity, RowGuidCol, primary key, or foreign key column
	//	    for (i = 0; i < entity.Fields.Count; i++) {
	//		field = (Field)entity.Fields[i];
	//
	//		// Is is an identity or uniqueidentifier column?
	//		if (field.IsIdentity || (options.GenerateProcsForForeignKey && (field.IsRowGuidCol || field.IsPrimaryKey || field.IsForeignKey))) {
	//
	//		    // Format the column name to make sure the first character is upper case
	//		    strColumnName = field.SqlName;
	//		    strColumnName = strColumnName.Substring(0, 1).ToUpper() + strColumnName.Substring(1);
	//				
	//		    // Create the SQL for the stored procedure
	//		    sb = new StringBuilder(1024);
	//
	//		    strProcName = options.GetProcName(entity.SqlObject, "SelectBy" + strColumnName.Replace(" ", "_"));
	//		    sb.Append("CREATE PROCEDURE " + strProcName + "\n\n");
	//
	//		    sb.Append("@" + field.SqlName.Replace(" ", "_") + "\t" + field.SqlType.Name);
	//		    sb.Append("\n\nAS\n\n");
	//		    sb.Append("SELECT\n\t*\n");
	//		    sb.Append("FROM\n\t[");
	//		    if (options.UseViews)
	//			sb.Append("vw");
	//		    sb.Append(entity.SqlObject);
	//		    sb.Append("]\n");
	//
	//		    sb.Append("WHERE \n");
	//		    sb.Append("\t[" + field.SqlName + "] = @" + field.SqlName.Replace(" ", "_") + "\n");
	//					
	//		    // Write out the stored procedure
	//		    WriteProcToFile(strProcName, sb.ToString() + "\nGO\n\n");
	//		    sb = null;
	//		}
	//	    }
	//				
	//	    /************************************************************************************/
	//	    // Create the stored procedure for a composite primary key
	//	    if (arrKeyList.Count > 1) {
	//		// Create the SQL for the stored procedure
	//		sb = new StringBuilder(1024);
	//
	//		strProcName = options.GetProcName(entity.SqlObject, "SelectBy" + strPrimaryKeyList);
	//		sb.Append("CREATE PROCEDURE " + strProcName + "\n\n");
	//				
	//		//// Is this a self-referencing key?
	//		//sb.Append("CREATE PROCEDURE proc" + entity.SqlObject.Replace(" ", "_") + "SelectBy" + strPrimaryKeyList + "\n\n");
	//
	//		// Create the parameter list
	//		for (i = 0; i < arrKeyList.Count; i++) {
	//		    field = (Field)arrKeyList[i];
	//		    sb.Append("@" + field.SqlName.Replace(" ", "_") + "\t" + field.SqlType.Name);
	//		    field = null;
	//					
	//		    if (i == arrKeyList.Count)
	//			sb.Append("\n");
	//		    else
	//			sb.Append(",\n");
	//		}
	//				
	//		sb.Append("\n\nAS\n\n");
	//		sb.Append("SELECT\n\t*\n");
	//		sb.Append("FROM\n\t[" + entity.SqlObject + "]\n");
	//		sb.Append("WHERE \n");
	//
	//		// Create the where clause
	//		for (i = 0; i < arrKeyList.Count; i++) {
	//		    field = (Field)entity.Fields[i];
	//		    sb.Append("\t[" + field.SqlName + "] = @" + field.SqlName.Replace(" ", "_"));
	//					
	//		    if (i != entity.Fields.Count)
	//			sb.Append(",\n");
	//		    else
	//			sb.Append("\n");
	//		}
	//				
	//		// Write out the stored procedure
	//		WriteProcToFile(strProcName, sb.ToString() + "\nGO\n\n");
	//		sb = null;
	//	    }
	//	}
	
	private void CreateView() {
	    StringBuilder sb = new StringBuilder();
	
//	    if (options.SingleFile) {
//		sb.Append("/*\n");
//		sb.Append("******************************************************************************\n");
//		sb.Append("******************************************************************************\n");
//		sb.Append("*/\n\n");
//	    }
	            
	    String strProcName = "vw" + sqlentity.Name;
	
	    sb.Append("if exists (select * from sysobjects where id = object_id(N'[" + strProcName + "]') and OBJECTPROPERTY(id, N'IsView') = 1)\n");
	    sb.Append("drop view [" + strProcName + "]\n");
	    sb.Append("GO\n");
	    sb.Append("\n");
	    sb.Append("CREATE VIEW " + strProcName + "\n");
	    sb.Append("\n");
	    sb.Append("AS\n");
	    sb.Append("\n");
	    sb.Append("SELECT ");
				
	    // Create the parameter list
	    Boolean first = true;
	    foreach (Column column in sqlentity.Columns) {
		if (!first) {
		    sb.Append(",\n");
		} else {
		    first=false;
		}
		sb.Append("\t" + EscapeSqlName(column.Name) + "");
	    }
	    sb.Append("\n");			
	    sb.Append("FROM\n\t");
	    sb.Append(EscapeSqlName(sqlentity.Name));
	    sb.Append("\n");
	
	    // Write out the stored procedure
	    FileInfo file = new FileInfo(options.RootDirectory + sqlentity.SqlScriptDirectory + "\\view\\" + strProcName + ".view.sql");
	    WriteToFile(file, sb.ToString() + "\nGO\n\n", sqlentity.SingleFile);
	    sb = null;
	}
	
	/// <summary>
	/// Internal helper method to write stored procedure to file based on options.SingleFile
	/// </summary>
	/// <param name="strStoredProcName">Name of stored procedure (used to name file if not outputting as single file)</param>
	/// <param name="strStoredProcText">Text to create stored procedure.</param>
	private void WriteProcToFile(String strStoredProcName, String strStoredProcText) {
	    StringBuilder sb = new StringBuilder();
	
	    if (sqlentity.SingleFile) {
		sb.Append("/*\n");
		sb.Append("******************************************************************************\n");
		sb.Append("******************************************************************************\n");
		sb.Append("*/\n\n");
	    }
	
	    if (sqlentity.ScriptDropStatement) {
		sb.Append("if exists (select * from dbo.sysobjects where id = object_id(N'[dbo].[" + strStoredProcName + "]') and OBJECTPROPERTY(id, N'IsProcedure') = 1)\n");
		sb.Append("drop procedure [dbo].[" + strStoredProcName + "]\n");
		sb.Append("GO\n\n");
	    }
	
	    sb.Append(strStoredProcText);
	
	    FileInfo file = new FileInfo(options.RootDirectory + sqlentity.SqlScriptDirectory + "\\proc\\" + strStoredProcName + ".proc.sql");
	    WriteToFile(file, sb.ToString(), sqlentity.SingleFile);
	}

	private void CreateTable() {
	    StringBuilder sb = new StringBuilder();

	    if (sqlentity.Description.Length>0) {
		sb.Append("/*\n");
		sb.Append(sqlentity.Description).Append("\n");
		sb.Append("*/\n\n");
	    }

	    // initial table script - if table does not already exist
	    sb.Append("if not exists (select * from dbo.sysobjects where id = object_id(N'[").Append(sqlentity.Name).Append("]') and OBJECTPROPERTY(id, N'IsUserTable') = 1)\n");
	    sb.Append("CREATE TABLE ").Append(EscapeSqlName(sqlentity.Name)).Append(" (\n");

	    Boolean first = true;
	    foreach (Column column in sqlentity.Columns) {
		if (!column.ViewColumn) {
		    if (!first) {
			sb.Append(",\n");
		    } else {
			first=false;
		    }
		    sb.Append("\t").Append(EscapeSqlName(column.Name)).Append(" ").Append(column.SqlType.Declaration);
		    if (column.Identity) {
			sb.Append(" IDENTITY(").Append(column.Seed).Append(",").Append(column.Increment).Append(")");
		    }
		    if (column.RowGuidCol) {
			sb.Append(" ROWGUIDCOL");
		    }
		    if (column.Identity || column.Required || column.RowGuidCol || column.Default.Length>0) {
			sb.Append(" NOT");
		    }
		    sb.Append(" NULL");
		    if (column.Default.Length>0) {
			sb.Append(" CONSTRAINT [DF_").Append(sqlentity.Name).Append("_").Append(column.Name).Append("] DEFAULT (").Append(column.Default).Append(")");
		    }
		}
	    }
	    sb.Append("\n)\n");
	    sb.Append("GO\n\n");

	    // create alter table scripts for each column - don't know when they got added
	    foreach (Column column in sqlentity.Columns) {
		if (!column.ViewColumn) {
		    if (column.Required && column.Default.Length==0) {
			sb.Append("/* -- commented out because column does not have default value and is required\n");
		    }
		    sb.Append("if not exists(select * from syscolumns where id=object_id('").Append(EscapeSqlName(sqlentity.Name)).Append("') and name = '").Append(column.Name).Append("')\n");
		    sb.Append("  BEGIN\n");
		    sb.Append("	ALTER TABLE ").Append(EscapeSqlName(sqlentity.Name)).Append(" ADD\n");
		    sb.Append("	    ").Append(EscapeSqlName(column.Name)).Append(" ").Append(column.SqlType.Declaration);
		    if (column.Identity) {
			sb.Append(" IDENTITY(").Append(column.Seed).Append(",").Append(column.Increment).Append(")");
		    }
		    if (column.RowGuidCol) {
			sb.Append(" ROWGUIDCOL");
		    }
		    if (column.Identity || column.Required || column.RowGuidCol || column.Default.Length>0) {
			sb.Append(" NOT");
		    }
		    sb.Append(" NULL\n");
		    if (column.Default.Length>0) {
			sb.Append("	 CONSTRAINT\n");
			sb.Append("		DF_").Append(sqlentity.Name).Append("_").Append(column.Name).Append(" DEFAULT ").Append(column.Default).Append(" WITH VALUES\n");
		    }
		    sb.Append("  END\n");
		    if (column.Required && column.Default.Length==0) {
			sb.Append("*/\n");
		    }
		    sb.Append("GO\n\n");
		}
	    }

	    // create constraints, checking for existance
	    foreach (Constraint constraint in sqlentity.Constraints) {
		if (!constraint.Type.ToUpper().Equals("CHECK")) {
		    sb.Append("if not exists (select * from dbo.sysobjects where id = object_id(N'").Append(EscapeSqlName(constraint.Name)).Append("') and OBJECTPROPERTY(id, N'");
		    if (constraint.Type.ToUpper().Equals("PRIMARY KEY")) {
			sb.Append("IsPrimaryKey");
		    } else if (constraint.Type.ToUpper().Equals("FOREIGN KEY")) {
			sb.Append("IsForeignKey");
		    } else if (constraint.Type.ToUpper().Equals("UNIQUE")) {
			sb.Append("IsUniqueCnst");
		    }
		    sb.Append("') = 1)\n");

		    sb.Append("ALTER TABLE ").Append(EscapeSqlName(sqlentity.Name)).Append(" ");
		    if (constraint.Type.ToUpper().Equals("PRIMARY KEY")) {
			sb.Append("WITH NOCHECK ");
		    }
		    sb.Append("ADD \n");
		    sb.Append("	CONSTRAINT ").Append(EscapeSqlName(constraint.Name)).Append(" ").Append(constraint.Type).Append(" ");
		    if (constraint.Type.ToUpper().Equals("PRIMARY KEY")) {
			if (constraint.Clustered) {
			    sb.Append("CLUSTERED ");
			} else {
			    sb.Append("NONCLUSTERED ");
			}
		    }
		    sb.Append("\n");
		    sb.Append("	(\n");

		    first = true;
		    foreach (Column column in constraint.Columns) {
			if (!first) {
			    sb.Append(",\n");
			} else {
			    first=false;
			}
			sb.Append("		").Append(EscapeSqlName(column.Name)).Append("");
		    }
		    sb.Append("\n	)");
		    if (constraint.Type.ToUpper().Equals("FOREIGN KEY")) {
			sb.Append(" REFERENCES ").Append(EscapeSqlName(constraint.ForeignEntity)).Append(" (\n");
			first=true;
			foreach (Column column in constraint.Columns) {
			    if (!first) {
				sb.Append(",\n");
			    } else {
				first=false;
			    }
			    sb.Append("		").Append(EscapeSqlName(column.ForeignColumn)).Append("");
			}
			sb.Append("\n	)");
		    }
		    sb.Append("\nGO\n\n");
		}
	    }

	    // create indexes, checking for existance
	    foreach (Index index in sqlentity.Indexes) {
		sb.Append("if not exists (select * from sysindexes where name='").Append(index.Name).Append("' and id=object_id(N'[").Append(sqlentity.Name).Append("]'))\n");

		sb.Append("CREATE");
		if (index.Unique) {
		    sb.Append(" UNIQUE");
		}
		sb.Append(" INDEX ").Append(EscapeSqlName(index.Name)).Append(" ON ").Append(EscapeSqlName(sqlentity.Name)).Append("\n");
		sb.Append("    (\n");
		first = true;
		foreach (Column column in index.Columns) {
		    if (!first) {
			sb.Append(",\n");
		    } else {
			first=false;
		    }
		    sb.Append("		").Append(EscapeSqlName(column.Name)).Append(" ").Append(column.SortDirection);
		}
		sb.Append("\n    )\n");
		sb.Append("GO\n");
	    }

	    FileInfo file = new FileInfo(options.RootDirectory + sqlentity.SqlScriptDirectory + "\\table\\" + sqlentity.Name + ".table.sql");
	    WriteToFile(file, sb.ToString(), sqlentity.SingleFile);
	    sb = null;
	}

	protected override void WriteRegion(StreamWriter writer, String text, String regionsString) {
	    writer.Write(text);
	    writer.Write(regionsString);
	}

	protected override String RegionTag {
	    get { return "-- #region"; }
	}

	protected override String EndRegionTag {
	    get { return "-- #endregion"; }
	}


	private String EscapeSqlName(String s) {
	    Boolean needsEscaping = false;

	    if (s.IndexOf(" ")>=0) {
		needsEscaping = true;
	    }
	    if (KEYWORDS.IndexOf("|"+s.ToLower()+"|")>=0) {
		needsEscaping = true;
	    }
	    
	    if (needsEscaping) {
		return "[" + s + "]";
	    } else {
		return s;
	    }
	}

    }
}

